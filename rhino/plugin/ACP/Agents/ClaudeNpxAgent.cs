using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

// Thin ACP (Agent Client Protocol) client. We are the *client*: we spawn Claude
// Code's ACP agent as a subprocess and drive it over newline-delimited JSON-RPC
// on its stdio. The agent does real work through our MCP server (handed to it in
// session/new), so ACP only carries the user's prompt in and progress out.
//
// PROTOTYPE simplifications, all worth revisiting:
//   * one shared agent for the whole app (no per-doc/per-slot isolation)
//   * auto-approves every permission request
//   * advertises no fs/terminal capabilities, so the agent never calls back for them
//   * no concurrency guard: a second prompt while one is running is sent as-is
internal sealed class ClaudeNpxAgent : IAgent
{
    // npm package that exposes Claude Code over ACP. Overridable for dev / Windows
    // (where `npx` isn't directly CreateProcess-able and needs a full path or cmd).
    const string DefaultCommand = "npx";
    const string DefaultPackage = "@agentclientprotocol/claude-agent-acp";

    public string Name => "acp";

    Process? Proc { get; set; }
    StreamWriter? Stdin { get; set; }
    string? SessionId { get; set; }
    int NextId { get; set; }

    Dictionary<int, TaskCompletionSource<JsonElement>> Pending { get; } = new();
    object Gate { get; } = new();
    SemaphoreSlim WriteGate { get; } = new(1, 1);
    Task? StartTask { get; set; }

    public async Task PromptAsync(UserMessage message, string mcpUrl, string cwd)
    {
        await EnsureStartedAsync(mcpUrl, cwd).ConfigureAwait(false);
        string sid = SessionId ?? throw new InvalidOperationException("ACP session not established.");
        await RequestAsync("session/prompt", new
        {
            sessionId = sid,
            prompt = new object[] { new { type = "text", text = message.Text } },
        }).ConfigureAwait(false);
    }

    Task EnsureStartedAsync(string mcpUrl, string cwd)
    {
        lock (Gate)
            return StartTask ??= StartGuardedAsync(mcpUrl, cwd);
    }

    async Task StartGuardedAsync(string mcpUrl, string cwd)
    {
        try
        {
            await StartAsync(mcpUrl, cwd).ConfigureAwait(false);
        }
        catch
        {
            lock (Gate)
                StartTask = null;   // let the next prompt retry from scratch
            Kill();
            throw;
        }
    }

    async Task StartAsync(string mcpUrl, string cwd)
    {
        string command = Environment.GetEnvironmentVariable("RHINO_MCP_ACP_COMMAND") ?? DefaultCommand;
        string package = Environment.GetEnvironmentVariable("RHINO_MCP_ACP_PACKAGE") ?? DefaultPackage;

        ProcessStartInfo psi = new()
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
        };
        psi.ArgumentList.Add(package);

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                RhinoApp.WriteLine($"[acp:err] {e.Data}");
        };
        proc.Start();
        proc.BeginErrorReadLine();

        Proc = proc;
        Stdin = proc.StandardInput;
        Stdin.NewLine = "\n";   // ACP frames messages with \n, not the platform newline
        _ = Task.Run(ReadLoopAsync);

        await RequestAsync("initialize", new
        {
            protocolVersion = 1,
            clientCapabilities = new
            {
                fs = new { readTextFile = false, writeTextFile = false },
                terminal = false,
            },
        }).ConfigureAwait(false);

        // The HTTP MCP entry is the agent's hands on Rhino. Adapter 0.39.0 validates
        // mcpServers as a union; the http variant requires `headers` to be present as
        // an array (empty is fine for a localhost listener), not just `type`+`url`.
        JsonElement session = await RequestAsync("session/new", new
        {
            cwd,
            mcpServers = new object[]
            {
                new { type = "http", name = "rhino", url = mcpUrl, headers = Array.Empty<object>() },
            },
        }).ConfigureAwait(false);

        SessionId = session.GetProperty("sessionId").GetString();
        RhinoApp.WriteLine($"[acp] session ready: {SessionId}");
    }

    async Task<JsonElement> RequestAsync(string method, object @params)
    {
        TaskCompletionSource<JsonElement> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int id;
        lock (Gate)
        {
            id = NextId++;
            Pending[id] = tcs;
        }
        await SendAsync(new { jsonrpc = "2.0", id, method, @params }).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    async Task SendAsync(object message)
    {
        StreamWriter writer = Stdin ?? throw new InvalidOperationException("ACP agent not started.");
        string json = JsonSerializer.Serialize(message, McpSerializer.Options);
        await WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(json).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    async Task ReadLoopAsync()
    {
        StreamReader reader = Proc!.StandardOutput;
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (line.Length == 0)
                continue;
            try
            {
                Dispatch(line);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[acp] dispatch error: {ex.Message}");
            }
        }
    }

    void Dispatch(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;

        bool hasId = root.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind is not JsonValueKind.Null;
        bool hasMethod = root.TryGetProperty("method", out JsonElement methodEl);

        if (hasMethod && hasId)                       // agent -> client request
        {
            _ = RespondAsync(idEl.Clone(), methodEl.GetString() ?? "", root.Clone());
            return;
        }
        if (hasMethod)                                // notification
        {
            HandleNotification(methodEl.GetString() ?? "", root);
            return;
        }
        if (!hasId)
            return;

        int id = idEl.GetInt32();                     // response to one of our requests
        TaskCompletionSource<JsonElement>? tcs;
        lock (Gate)
            Pending.Remove(id, out tcs);
        if (tcs is null)
            return;

        if (root.TryGetProperty("error", out JsonElement err))
            tcs.TrySetException(new Exception($"ACP error: {err.GetRawText()}"));
        else if (root.TryGetProperty("result", out JsonElement res))
            tcs.TrySetResult(res.Clone());
        else
            tcs.TrySetResult(default);
    }

    void HandleNotification(string method, JsonElement root)
    {
        if (method != "session/update")
            return;
        if (!root.TryGetProperty("params", out JsonElement p) || !p.TryGetProperty("update", out JsonElement update))
            return;
        if (!update.TryGetProperty("sessionUpdate", out JsonElement kindEl))
            return;

        switch (kindEl.GetString())
        {
            case "agent_message_chunk":
            case "agent_thought_chunk":
                if (update.TryGetProperty("content", out JsonElement content) &&
                    content.TryGetProperty("text", out JsonElement text))
                    RhinoApp.Write(text.GetString());
                break;
            case "tool_call":
                if (update.TryGetProperty("title", out JsonElement title))
                    RhinoApp.WriteLine($"\n[acp] ⚙ {title.GetString()}");
                break;
        }
    }

    async Task RespondAsync(JsonElement id, string method, JsonElement request)
    {
        try
        {
            object result = method == "session/request_permission"
                ? AutoApprove(request)
                : new { };
            await SendAsync(new { jsonrpc = "2.0", id, result }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[acp] failed to answer {method}: {ex.Message}");
        }
    }

    static object AutoApprove(JsonElement request)
    {
        string optionId = "allow";
        if (request.TryGetProperty("params", out JsonElement p) &&
            p.TryGetProperty("options", out JsonElement options) &&
            options.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement option in options.EnumerateArray())
            {
                if (!option.TryGetProperty("optionId", out JsonElement oid) || oid.GetString() is not string id)
                    continue;
                optionId = id;
                string? kind = option.TryGetProperty("kind", out JsonElement k) ? k.GetString() : null;
                if (kind is not null && kind.StartsWith("allow"))
                    break;
            }
        }
        return new { outcome = new { outcome = "selected", optionId } };
    }

    void Kill()
    {
        try
        {
            if (Proc is { HasExited: false })
                Proc.Kill(entireProcessTree: true);
        }
        catch { }
        Proc = null;
        Stdin = null;
    }

    public void Cancel() => Kill();

    public void Dispose() => Kill();
}
