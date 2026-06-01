using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from the global RhMcp.Server.ContentBlock

namespace RhMcp;

// Claude Code as an in-process ACP agent: owns a long-lived `claude` process in stream-json mode,
// frames one user turn per prompt on stdin, and translates its event stream into ACP session/update
// notifications pushed to the connected IAcpClient. The terminal `result` event ends the turn.
// (Ported from the former ClaudeCliAgent; same launch contract and parsing, ACP-shaped output.)
internal sealed class ClaudeAcpAgent : IAcpAgent, IDisposable
{
    private AgentDefinition Definition { get; }
    private IAcpClient Client { get; }
    private string Cwd { get; }

    private Process? Proc { get; set; }
    private StreamWriter? Stdin { get; set; }
    private Task? StartTask { get; set; }
    private TaskCompletionSource<StopReason>? CurrentTurn { get; set; }

    private object Gate { get; } = new();
    private SemaphoreSlim WriteGate { get; } = new(1, 1);
    private SemaphoreSlim TurnGate { get; } = new(1, 1);

    // Stable session id so a respawn (after cancel/crash) resumes the same Claude conversation.
    private Guid SessionId { get; } = Guid.NewGuid();
    private string SessionIdText => SessionId.ToString();
    private bool Started { get; set; }
    private string McpUrl { get; set; } = string.Empty;

    public ClaudeAcpAgent(AgentDefinition def, IAcpClient client, string cwd)
    {
        Definition = def;
        Client = client;
        Cwd = cwd;
    }

    public ValueTask<InitializeResponse> InitializeAsync(InitializeRequest request, CancellationToken cancellationToken = default) =>
        new(new InitializeResponse { ProtocolVersion = ProtocolConstants.Version });

    public ValueTask<NewSessionResponse> SessionNewAsync(NewSessionRequest request, CancellationToken cancellationToken = default)
    {
        foreach (Acp.McpServer server in request.McpServers)
            if (server is HttpMcpServer http && http.Name == "rhino")
                McpUrl = http.Url;
        return new(new NewSessionResponse { SessionId = SessionIdText });
    }

    public async ValueTask<PromptResponse> SessionPromptAsync(PromptRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync().ConfigureAwait(false);

        await TurnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TaskCompletionSource<StopReason> turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gate)
                CurrentTurn = turn;

            await SendTurnAsync(request.Prompt).ConfigureAwait(false);
            StopReason reason = await turn.Task.ConfigureAwait(false);
            return new PromptResponse { StopReason = reason };
        }
        finally
        {
            lock (Gate)
                CurrentTurn = null;
            TurnGate.Release();
        }
    }

    // Resolve the in-flight turn as a clean cancel before tearing the process down, so a deliberate
    // cancel ends the turn with StopReason.Cancelled instead of faulting into the IOException the
    // read loop raises on exit (which AgentDispatch would print as a user-visible error line). This
    // wins the TCS first; the read loop's later TrySetException is then a no-op. The next prompt
    // respawns with --resume.
    public ValueTask SessionCancelAsync(CancelNotification notification, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<StopReason>? turn;
        lock (Gate)
            turn = CurrentTurn;
        turn?.TrySetResult(StopReason.Cancelled);
        Kill();
        return default;
    }

    public ValueTask<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest request, CancellationToken cancellationToken = default) =>
        throw NotSupported("authenticate");

    public ValueTask<LoadSessionResponse> SessionLoadAsync(LoadSessionRequest request, CancellationToken cancellationToken = default) =>
        throw NotSupported("session/load");

    public ValueTask<SetSessionModeResponse> SessionSetModeAsync(SetSessionModeRequest request, CancellationToken cancellationToken = default) =>
        throw NotSupported("session/set_mode");

    public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
        throw NotSupported(method);

    public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) => default;

    private static AcpException NotSupported(string method) =>
        new($"'{method}' is not supported by the Claude agent", (int)Acp.JsonRpcErrorCode.MethodNotFound);

    // ---- launch + stdin -----------------------------------------------------------------------

    private Task EnsureStartedAsync()
    {
        lock (Gate)
            return StartTask ??= StartGuardedAsync();
    }

    private async Task StartGuardedAsync()
    {
        try
        {
            await StartAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            lock (Gate)
                StartTask = null; // let the next prompt retry from scratch
            Kill();
            throw;
        }
    }

    private Task StartAsync()
    {
        if (!TryResolveCommand(out string path))
            throw new FileNotFoundException("Claude CLI not found. Install Claude Code (claude.ai/install).");

        ProcessStartInfo psi = new()
        {
            FileName = path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Cwd,
        };
        ConfigureArguments(psi);

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                RhinoApp.WriteLine($"[{Definition.Name}:err] {e.Data}");
        };
        proc.Start();
        proc.BeginErrorReadLine();

        Proc = proc;
        Stdin = proc.StandardInput;
        Stdin.NewLine = "\n"; // stream-json input is newline-framed, not platform-framed
        Started = true;
        _ = Task.Run(() => ReadLoopAsync(proc));
        return Task.CompletedTask;
    }

    private void ConfigureArguments(ProcessStartInfo psi)
    {
        // Same {"mcpServers":{...}} shape Claude Code expects; rhino points at this doc's HTTP
        // listener (not the router) so the agent always operates on the exact doc.
        JsonObject servers = new()
        {
            ["rhino"] = new JsonObject { ["type"] = "http", ["url"] = McpUrl },
        };
        if (TryGetExtraMcpServers(out JsonObject extra))
            foreach (KeyValuePair<string, JsonNode?> entry in extra)
                if (entry.Key != "rhino" && entry.Value is JsonNode node)
                    servers[entry.Key] = node.DeepClone();

        string mcpConfig = new JsonObject { ["mcpServers"] = servers }.ToJsonString(McpSerializer.Options);

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--input-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--mcp-config");
        psi.ArgumentList.Add(mcpConfig);
        psi.ArgumentList.Add("--strict-mcp-config");
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("mcp__rhino*");
        psi.ArgumentList.Add("--append-system-prompt");
        psi.ArgumentList.Add(ComposedSystemPrompt);
        psi.ArgumentList.Add("--disable-slash-commands");
        psi.ArgumentList.Add(Started ? "--resume" : "--session-id");
        psi.ArgumentList.Add(SessionIdText);

        if (Definition.Model.Length > 0)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(Definition.Model);
        }
        foreach (string arg in Definition.ExtraArgs)
            psi.ArgumentList.Add(arg);
    }

    private string ComposedSystemPrompt => AgentPrompts.Compose(Definition.SystemPrompt);

    private bool TryResolveCommand(out string path)
    {
        path = string.Empty;
        foreach (string candidate in Definition.SearchPaths)
            if (File.Exists(candidate))
                path = candidate; // last match wins, preserving the original ordering
        return path.Length > 0;
    }

    // Fail-soft parse of AISettings.ExtraMcpServersJson down to its inner `mcpServers` object,
    // detached so children can be reparented. A bad textarea must never fault the launch.
    private static bool TryGetExtraMcpServers(out JsonObject servers)
    {
        servers = new JsonObject();
        string json;
        try
        {
            json = AISettings.ExtraMcpServersJson;
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["mcpServers"] is not JsonObject inner)
                return false;
            foreach (KeyValuePair<string, JsonNode?> entry in inner)
                if (entry.Value is JsonNode node)
                    servers[entry.Key] = node.DeepClone();
            return servers.Count > 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private async Task SendTurnAsync(ContentBlock[] prompt)
    {
        StreamWriter writer = Stdin ?? throw new InvalidOperationException("Claude agent not started.");
        string line = FormatTurn(prompt);

        await WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    // ACP content blocks -> Claude's stream-json user content (text + base64 image). Underscored
    // property names survive the camelCase policy, so media_type stays media_type.
    private static string FormatTurn(ContentBlock[] prompt)
    {
        List<object> content = new();
        foreach (ContentBlock block in prompt)
        {
            switch (block)
            {
                case TextContentBlock text:
                    content.Add(new { type = "text", text = text.Text });
                    break;
                case ImageContentBlock image:
                    content.Add(new
                    {
                        type = "image",
                        source = new { type = "base64", media_type = image.MimeType, data = image.Data },
                    });
                    break;
            }
        }

        return JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content },
        }, McpSerializer.Options);
    }

    // ---- read + translate ---------------------------------------------------------------------

    private async Task ReadLoopAsync(Process proc)
    {
        try
        {
            StreamReader reader = proc.StandardOutput;
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                    continue;
                try
                {
                    Handle(line, proc);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"[{Definition.Name}] parse error: {ex.Message}");
                }
            }
        }
        finally
        {
            TaskCompletionSource<StopReason>? turn = null;
            lock (Gate)
            {
                if (ReferenceEquals(Proc, proc))
                {
                    turn = CurrentTurn;
                    CurrentTurn = null;
                    Proc = null;
                    Stdin = null;
                    StartTask = null;
                }
            }
            turn?.TrySetException(new IOException($"{Definition.Name} process exited."));
        }
    }

    private void Handle(string line, Process proc)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("type", out JsonElement typeEl))
            return;

        switch (typeEl.GetString())
        {
            case "assistant":
                EmitAssistant(root);
                break;
            case "user":
                EmitToolResults(root);
                break;
            case "result":
                CompleteTurn(proc, StopReason.EndTurn);
                break;
        }
    }

    private void EmitAssistant(JsonElement root)
    {
        if (!TryGetContent(root, out JsonElement content))
            return;

        foreach (JsonElement block in content.EnumerateArray())
        {
            switch (block.TryGetProperty("type", out JsonElement bt) ? bt.GetString() : null)
            {
                case "text" when block.TryGetProperty("text", out JsonElement text):
                    Push(new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = text.GetString() ?? string.Empty } });
                    break;
                case "tool_use":
                    Push(new ToolCallSessionUpdate
                    {
                        ToolCallId = block.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? string.Empty : string.Empty,
                        Title = block.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty,
                        RawInput = block.TryGetProperty("input", out JsonElement input) ? input.Clone() : null,
                    });
                    break;
            }
        }
    }

    private void EmitToolResults(JsonElement root)
    {
        if (!TryGetContent(root, out JsonElement content))
            return;

        foreach (JsonElement block in content.EnumerateArray())
        {
            if ((block.TryGetProperty("type", out JsonElement bt) ? bt.GetString() : null) != "tool_result")
                continue;
            Push(new ToolCallUpdateSessionUpdate
            {
                ToolCallId = block.TryGetProperty("tool_use_id", out JsonElement id) ? id.GetString() ?? string.Empty : string.Empty,
                Status = ToolCallStatus.Completed,
                RawOutput = block.TryGetProperty("content", out JsonElement output) ? output.Clone() : null,
            });
        }
    }

    private static bool TryGetContent(JsonElement root, out JsonElement content)
    {
        content = default;
        return root.TryGetProperty("message", out JsonElement message)
            && message.TryGetProperty("content", out content)
            && content.ValueKind == JsonValueKind.Array;
    }

    // Our IAcpClient handler is synchronous, so this completes inline; elements are cloned above so
    // they outlive the parsed document.
    private void Push(SessionUpdate update) =>
        _ = Client.SessionUpdateAsync(new SessionNotification { SessionId = SessionIdText, Update = update });

    private void CompleteTurn(Process proc, StopReason reason)
    {
        TaskCompletionSource<StopReason>? turn = null;
        lock (Gate)
            if (ReferenceEquals(Proc, proc))
                turn = CurrentTurn;
        turn?.TrySetResult(reason);
    }

    private void Kill()
    {
        Process? proc;
        lock (Gate)
        {
            proc = Proc;
            StartTask = null;
        }
        try
        {
            if (proc is { HasExited: false })
                proc.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // process already gone
        }
    }

    public void Dispose()
    {
        Kill();
        TaskCompletionSource<StopReason>? turn;
        lock (Gate)
            turn = CurrentTurn;
        turn?.TrySetException(new ObjectDisposedException(nameof(ClaudeAcpAgent)));
        // WriteGate/TurnGate are deliberately not disposed: the faulted turn above still runs its
        // finally (TurnGate.Release), and disposing here would race it into ObjectDisposedException
        // ("System.Threading.SemaphoreSlim"). Matches CliAgent, which never disposes its gates.
    }
}
