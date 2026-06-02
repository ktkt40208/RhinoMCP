using System;
using System.IO;
using System.Threading.Tasks;

namespace RhMcp;

// The single funnel every surface (command, command-line interceptor, panel) routes through,
// so they all drive one active agent per doc against one shared conversation. Resolves the
// active agent, ensures an MCP listener for the doc, then fires the turn off-thread.
internal static class AgentDispatch
{
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

        _ = agent.PromptAsync(message, url, cwd).ContinueWith(
            t =>
            {
                Exception error = t.Exception!.GetBaseException();
                if (error is ObjectDisposedException)
                    return;   // agent torn down mid-turn (New conversation / Stop) — expected, not an error
                RhinoApp.WriteLine($"[{agent.Name}] error: {error.Message}");
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
