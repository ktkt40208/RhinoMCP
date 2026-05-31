using System.IO;
using System.Threading.Tasks;
using Rhino.Commands;
using Rhino.Input.Custom;

namespace RhMcp;

// Shared flow for the per-agent commands (Claude, Cursor, Gemini, Codex). Abstract so Rhino's
// command discovery skips it and only instantiates the concrete subclasses below.
public abstract class AgentCommand : Command
{
    protected override string CommandContextHelpUrl => "https://mcneel.github.io/RhinoMCP";

    // The agent this command drives; a fresh instance, deduplicated per document by AgentHost.
    // private protected keeps it within IAgent's internal accessibility.
    private protected abstract IAgent CreateAgent();

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // NOTE : On Rhino 8 Mac Get Literal String doesn't work so idk
        GetString get = new();

        if (get.GetLiteralString() != Rhino.Input.GetResult.String) return Result.Cancel;
        string request = get.StringResult();
        if (string.IsNullOrWhiteSpace(request)) return Result.Cancel;

        IAgent agent = AgentHost.For(doc, CreateAgent);

        // The agent needs an MCP listener as its hands; auto-start one for this doc if absent.
        if (!RhinoMcpHost.TryGetPortFor(doc, out int port))
        {
            RhinoMcpHost.StartOrRestart(doc, RhinoMcpHost.GetNextPort());
            if (!RhinoMcpHost.TryGetPortFor(doc, out port))
            {
                RhinoApp.WriteLine($"[{agent.Name}] could not start an MCP server for this document.");
                return Result.Failure;
            }
        }

        string url = $"http://localhost:{port}/";
        string cwd = !string.IsNullOrEmpty(doc.Path)
            ? Path.GetDirectoryName(doc.Path) ?? Path.GetTempPath()
            : Path.GetTempPath();

        _ = agent.PromptAsync(request, url, cwd).ContinueWith(
            t => RhinoApp.WriteLine($"[{agent.Name}] error: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);

        return Result.Success;
    }
}
