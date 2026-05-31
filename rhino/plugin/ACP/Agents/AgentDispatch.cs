using System.IO;
using System.Threading.Tasks;

namespace RhMcp;

// The single funnel every surface (command, command-line interceptor, panel) routes through,
// so they all drive one active agent per doc against one shared conversation. Resolves the
// active agent, ensures an MCP listener for the doc, then fires the turn off-thread.
internal static class AgentDispatch
{
    public static void PromptActive(RhinoDoc doc, UserMessage message)
    {
        if (!AgentHost.TryFor(doc, out IAgent agent))
        {
            RhinoApp.WriteLine("No agent available — open AI Settings to configure one.");
            return;
        }

        // The agent needs an MCP listener as its hands; auto-start one for this doc if absent.
        if (!RhinoMcpHost.TryGetPortFor(doc, out int port))
        {
            RhinoMcpHost.StartOrRestart(doc, RhinoMcpHost.GetNextPort());
            if (!RhinoMcpHost.TryGetPortFor(doc, out port))
            {
                RhinoApp.WriteLine($"[{agent.Name}] could not start an MCP server for this document.");
                return;
            }
        }

        string url = $"http://localhost:{port}/";
        string cwd = !string.IsNullOrEmpty(doc.Path)
            ? Path.GetDirectoryName(doc.Path) ?? Path.GetTempPath()
            : Path.GetTempPath();

        _ = agent.PromptAsync(message, url, cwd).ContinueWith(
            t => RhinoApp.WriteLine($"[{agent.Name}] error: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
