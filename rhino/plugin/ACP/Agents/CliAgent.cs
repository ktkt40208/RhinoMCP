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
    public abstract string Name { get; }

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

    // The executable's leaf name, probed across the known install dirs (see TryResolveCommand).
    protected abstract string CommandFileName { get; }

    // Thrown when the CLI isn't installed; should point the user at the installer.
    protected abstract string NotFoundMessage { get; }

    // Append the CLI's launch args (and any --resume vs fresh-session flag, keyed off Started).
    protected abstract void ConfigureArguments(ProcessStartInfo psi, string mcpUrl);

    // The single stdin line that carries one user turn (a JSON envelope or plain text).
    protected abstract string FormatUserMessage(string text);

    // Parse one stdout line; call CompleteTurn(proc) when the turn's terminal event arrives.
    protected abstract void Handle(string line, Process proc);

    public async Task PromptAsync(string request, string mcpUrl, string cwd)
    {
        await EnsureStartedAsync(mcpUrl, cwd).ConfigureAwait(false);

        await TurnGate.WaitAsync().ConfigureAwait(false);
        try
        {
            TaskCompletionSource<bool> turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gate)
                CurrentTurn = turn;

            await SendUserMessageAsync(request).ConfigureAwait(false);
            await turn.Task.ConfigureAwait(false);   // completes when the reader sees this turn's terminal event
        }
        finally
        {
            lock (Gate)
                CurrentTurn = null;
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
        catch
        {
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

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                RhinoApp.WriteLine($"[{Name}:err] {e.Data}");
        };
        proc.Start();
        proc.BeginErrorReadLine();

        Proc = proc;
        Stdin = proc.StandardInput;
        Stdin.NewLine = "\n";   // stream-json input is newline-framed, not platform-framed
        Started = true;
        _ = Task.Run(() => ReadLoopAsync(proc));
        return Task.CompletedTask;
    }

    async Task SendUserMessageAsync(string text)
    {
        StreamWriter writer = Stdin ?? throw new InvalidOperationException($"{Name} agent not started.");
        string line = FormatUserMessage(text);

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
    // native installer dir, so we probe the known locations rather than relying on PATH.
    bool TryResolveCommand(out string path)
    {
        path = string.Empty;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        {
            Path.Combine(home, ".local", "bin", CommandFileName),
            $"/opt/homebrew/bin/{CommandFileName}",
            $"/usr/local/bin/{CommandFileName}",
        };

        foreach (string candidate in candidates)
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
                proc.Kill(entireProcessTree: true);   // the CLI spawns MCP/helper children
        }
        catch { }
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
