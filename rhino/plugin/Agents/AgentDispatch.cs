using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

// The single funnel every surface (command, command-line interceptor, panel) routes through,
// so they all drive one active agent per doc against one shared conversation. Resolves the
// active agent, ensures an MCP listener for the doc, then fires the turn off-thread.
internal static class AgentDispatch
{
    // One slot per doc, taken for the full Open..run..Close of a turn. It serializes the undo-record
    // bracket itself (BeginUndoRecord must never run while another turn's record is open, or Rhino
    // declines it and that turn's mutations land in the wrong record), and it is the funnel-level
    // one-turn-at-a-time guard the panel's Send/Stop button only enforces at the UI: the interceptor
    // and AgentCommand reach PromptActive directly, so the guard has to live here, not on the button.
    private static Dictionary<uint, SemaphoreSlim> DocGates { get; } = new();

    static AgentDispatch()
    {
        // Forget a closed doc's gate so the map doesn't accumulate one slot per doc opened this
        // session. Not disposed: an in-flight turn's finally still runs gate.Release() on the same
        // object reference, and disposing would race it into ObjectDisposedException.
        RhinoDoc.CloseDocument += (_, e) =>
        {
            lock (DocGates)
                DocGates.Remove(e.DocumentSerialNumber);
        };
    }

    private static SemaphoreSlim GateFor(uint docSerial)
    {
        lock (DocGates)
        {
            if (!DocGates.TryGetValue(docSerial, out SemaphoreSlim? gate))
            {
                gate = new SemaphoreSlim(1, 1);
                DocGates[docSerial] = gate;
            }
            return gate;
        }
    }

    // The agent needs an MCP listener as its hands; auto-start one for this doc if absent so the
    // happy path needs zero setup. Shared by panel-open (warm the listener the moment an agent is
    // available) and the prompt path (the safety net if open didn't run). Idempotent: a started
    // listener is reused. Worked-or-not so callers can stay silent on the warm-up path.
    public static bool TryEnsureListener(RhinoDoc doc, out int port)
    {
        if (RhinoMcpHost.TryGetPortFor(doc, out port))
            return true;

        if (!RhinoMcpHost.TryGetNextPort(out int nextPort))
            return false;

        RhinoMcpHost.StartOrRestart(doc, nextPort);
        return RhinoMcpHost.TryGetPortFor(doc, out port);
    }

    public static void PromptActive(RhinoDoc doc, UserMessage message)
    {
        if (!AgentHost.TryFor(doc, out IAgentRunner agent))
        {
            RhinoApp.WriteLine("No agent available — open AI Settings to configure one.");
            return;
        }

        if (!TryEnsureListener(doc, out int port))
        {
            RhinoApp.WriteLine($"[{agent.Name}] could not start an MCP server for this document.");
            return;
        }

        string url = $"http://localhost:{port}/agent";
        string cwd = !string.IsNullOrEmpty(doc.Path)
            ? Path.GetDirectoryName(doc.Path) ?? Path.GetTempPath()
            : Path.GetTempPath();

        // Reject (don't queue) a second prompt while this doc's turn is running. Wait(0) is the
        // funnel guard: a queued turn would silently merge two users' intent into one undo record
        // and one session, and the interceptor/AgentCommand have no Stop button to lean on.
        SemaphoreSlim gate = GateFor(doc.RuntimeSerialNumber);
        if (!gate.Wait(0))
        {
            RhinoApp.WriteLine($"[{agent.Name}] a turn is already running for this document — wait for it to finish or Stop it.");
            return;
        }

        // Fire-and-forget, but never unobserved: a fault from Open/Close/prompt is surfaced here so a
        // dropped turn is visible rather than swallowed by a discarded Task.
        _ = RunTurnAsync(agent, doc, message, url, cwd, gate)
            .ContinueWith(
                t => RhinoApp.WriteLine($"[{agent.Name}] turn faulted: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    // Bracket the whole turn in one undo record so a single Ctrl+Z reverts every mutation it made.
    // The doc gate is already held (acquired in PromptActive), so the record is opened only when no
    // other turn's record is open. Open before the prompt fires (the first tool call must land inside
    // the record); close in the finally so it is ALWAYS ended, whether the turn completes, is
    // cancelled, or faults, and release the gate in the same finally so Open..Close is atomic.
    private static async Task RunTurnAsync(IAgentRunner agent, RhinoDoc doc, UserMessage message, string url, string cwd, SemaphoreSlim gate)
    {
        TurnUndoCheckpoint? checkpoint = null;
        try
        {
            checkpoint = await TurnUndoCheckpoint.OpenAsync(doc, TurnUndoCheckpoint.Describe(message.Text)).ConfigureAwait(false);
            await agent.PromptAsync(message, url, cwd).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Agent torn down mid-turn (New conversation / Stop): expected, not an error.
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[{agent.Name}] error: {ex.Message}");
        }
        finally
        {
            if (checkpoint is not null)
                await checkpoint.CloseAsync().ConfigureAwait(false);
            gate.Release();
        }
    }
}
