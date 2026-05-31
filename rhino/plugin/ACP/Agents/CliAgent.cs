using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

// Shared lifecycle for stream-json CLI agents (Claude, Codex): spawn a long-lived
// process, frame one user turn per prompt on stdin, and read a JSON event stream off
// stdout until the turn's terminal event arrives. Subclasses supply only the CLI's
// specifics — its binary, its launch args, how a turn is framed, and how its event
// stream is parsed.
internal abstract class CliAgent : IAgent
{
    protected AgentDefinition Definition { get; }

    public string Name => Definition.Name;

    Process? Proc { get; set; }
    StreamWriter? Stdin { get; set; }
    Task? StartTask { get; set; }
    TaskCompletionSource<bool>? CurrentTurn { get; set; }

    object Gate { get; } = new();
    SemaphoreSlim WriteGate { get; } = new(1, 1);   // serializes raw stdin writes
    SemaphoreSlim TurnGate { get; } = new(1, 1);    // serializes turns (one at a time)

    // Stable session id so a respawn (after cancel/crash) can resume the same
    // conversation. First start opens it fresh; later restarts resume it.
    protected Guid SessionId { get; } = Guid.NewGuid();
    protected bool Started { get; private set; }

    // In-memory transcript of this agent's turns for this Rhino session. The read hook
    // for a future panel/export; not yet surfaced on IAgent.
    internal Conversation Conversation { get; }

    protected CliAgent(AgentDefinition def)
    {
        Definition = def;
        Conversation = new Conversation(SessionId);
    }

    // Thrown when the CLI isn't installed; should point the user at the installer.
    protected abstract string NotFoundMessage { get; }

    // Append the CLI's launch args (and any --resume vs fresh-session flag, keyed off Started).
    protected abstract void ConfigureArguments(ProcessStartInfo psi, string mcpUrl);

    // Adapters call this after their own flags so custom entries can inject extra args.
    // Built-ins set ExtraArgs=[] so this emits nothing and the launch is unchanged.
    protected void AppendExtraArgs(ProcessStartInfo psi)
    {
        foreach (string arg in Definition.ExtraArgs)
            psi.ArgumentList.Add(arg);
    }

    // The built-in AskUserQuestion needs an interactive frontend we don't have in headless
    // stdio mode, so it auto-cancels; steer every agent to the Rhino MCP tool that renders on
    // the command line instead. Subclasses inject this through their CLI's system-prompt flag.
    protected static string AskUserSteer =>
        "To ask the user a question or have them choose between options, always call the "
        + $"mcp__{RouterMcpConfig.ServerName}__ask_user tool. Never use the built-in AskUserQuestion "
        + "tool — it cannot be displayed in this environment and will be cancelled.";

    // The single stdin line that carries one user turn (a JSON envelope or plain text).
    protected abstract string FormatUserMessage(UserMessage message);

    // Parse one stdout line; call CompleteTurn(proc) when the turn's terminal event arrives.
    protected abstract void Handle(string line, Process proc);

    // Echo to the command line *and* record into the transcript. Subclasses route their
    // parsed events through these rather than calling RhinoApp.* directly, so capture and
    // the on-screen formatting live in one place.
    protected void EmitSessionStarted()
    {
        RhinoApp.WriteLine($"[{Name}] session started.");
        Conversation.NoteSessionStarted();
    }

    protected void EmitAssistantText(string? text)
    {
        RhinoApp.Write(text);
        Conversation.Record(TurnEventKind.AssistantText, text ?? string.Empty);
    }

    protected void EmitToolUse(string? name)
    {
        RhinoApp.WriteLine($"\n[{Name}] ⚙ {name}");
        Conversation.Record(TurnEventKind.ToolUse, name ?? string.Empty);
    }

    protected void EmitResult(string? text)
    {
        RhinoApp.WriteLine($"\n[{Name}] {text}");
        Conversation.Record(TurnEventKind.Result, text ?? string.Empty);
    }

    // Debug-only tracer for the spawn/stdin/stdout/exit lifecycle. [Conditional] strips the
    // call and its argument evaluation in Release, so the raw-line dumps below cost nothing
    // when not debugging.
    [Conditional("DEBUG")]
    protected void DebugLog(string message) => RhinoApp.WriteLine($"[{Name}:debug] {message}");

    public async Task PromptAsync(UserMessage message, string mcpUrl, string cwd)
    {
        await EnsureStartedAsync(mcpUrl, cwd).ConfigureAwait(false);

        await TurnGate.WaitAsync().ConfigureAwait(false);
        try
        {
            TaskCompletionSource<bool> turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gate)
                CurrentTurn = turn;
            Conversation.BeginTurn(message.Text);

            await SendUserMessageAsync(message).ConfigureAwait(false);
            await turn.Task.ConfigureAwait(false);   // completes when the reader sees this turn's terminal event
        }
        finally
        {
            lock (Gate)
                CurrentTurn = null;
            Conversation.CompleteTurn();   // single robust completion point: runs on success and fault alike
            TurnGate.Release();
        }
    }

    // Kill the process; the reader loop faults the in-flight turn and resets state so
    // the next prompt respawns with --resume, continuing the same session.
    public void Cancel() => Kill();

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
        catch (Exception ex)
        {
            DebugLog($"start failed: {ex.GetType().Name}: {ex.Message}");
            lock (Gate)
                StartTask = null;   // let the next prompt retry from scratch
            Kill();
            throw;
        }
    }

    Task StartAsync(string mcpUrl, string cwd)
    {
        if (!TryResolveCommand(out string path))
            throw new FileNotFoundException(NotFoundMessage);

        ProcessStartInfo psi = new()
        {
            FileName = path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
        };
        ConfigureArguments(psi, mcpUrl);   // reads Started for the resume-vs-fresh flag
        DebugLog($"spawn: {path} {string.Join(" ", psi.ArgumentList)}");
        DebugLog($"cwd={cwd} mcp={mcpUrl} resume={Started}");

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                RhinoApp.WriteLine($"[{Name}:err] {e.Data}");
        };
        proc.Start();
        proc.BeginErrorReadLine();
        DebugLog($"started pid {proc.Id}");

        Proc = proc;
        Stdin = proc.StandardInput;
        Stdin.NewLine = "\n";   // stream-json input is newline-framed, not platform-framed
        Started = true;
        _ = Task.Run(() => ReadLoopAsync(proc));
        return Task.CompletedTask;
    }

    async Task SendUserMessageAsync(UserMessage message)
    {
        StreamWriter writer = Stdin ?? throw new InvalidOperationException($"{Name} agent not started.");
        string line = FormatUserMessage(message);
        DebugLog($">> {line}");

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

    async Task ReadLoopAsync(Process proc)
    {
        try
        {
            StreamReader reader = proc.StandardOutput;
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                    continue;
                DebugLog($"<< {line}");
                try
                {
                    Handle(line, proc);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"[{Name}] parse error: {ex.Message}");
                }
            }
        }
        finally
        {
            DebugLog($"reader loop ended ({(proc.HasExited ? $"exit code {proc.ExitCode}" : "process still alive")})");

            // Process died (cancel, crash, or exit). Only touch shared state if we still
            // own the current process — a respawn may already have replaced it. Fault any
            // in-flight turn so the awaiting prompt fails instead of hanging.
            TaskCompletionSource<bool>? turn = null;
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
            turn?.TrySetException(new IOException($"{Name} process exited."));
        }
    }

    protected void CompleteTurn(Process proc)
    {
        TaskCompletionSource<bool>? turn = null;
        lock (Gate)
        {
            if (ReferenceEquals(Proc, proc))
                turn = CurrentTurn;
        }
        turn?.TrySetResult(true);
    }

    // Rhino launched from Finder/Dock often has a minimal PATH without Homebrew or the
    // native installer dir, so we probe the definition's locations rather than relying on PATH.
    // Last match wins (later candidates override earlier ones), preserving the original ordering.
    bool TryResolveCommand(out string path)
    {
        path = string.Empty;
        foreach (string candidate in Definition.SearchPaths)
        {
            if (!File.Exists(candidate)) continue;
            path = candidate;
        }

        return !string.IsNullOrEmpty(path);
    }

    void Kill()
    {
        Process? proc;
        lock (Gate)
        {
            proc = Proc;
            StartTask = null;   // next prompt respawns
        }
        try
        {
            if (proc is { HasExited: false })
            {
                DebugLog($"killing pid {proc.Id}");
                proc.Kill(entireProcessTree: true);   // the CLI spawns MCP/helper children
            }
        }
        catch (Exception ex)
        {
            DebugLog($"kill failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Kill();
        TaskCompletionSource<bool>? turn;
        lock (Gate)
            turn = CurrentTurn;
        turn?.TrySetException(new ObjectDisposedException(GetType().Name));
    }
}
