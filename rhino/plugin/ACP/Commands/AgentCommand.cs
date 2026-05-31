using Rhino.Commands;
using Rhino.Input.Custom;

namespace RhMcp;

// Shared flow for the per-agent commands (Claude, Codex). Abstract so Rhino's command discovery
// skips it and only instantiates the concrete subclasses below. Each command sets its adapter as
// the doc's active agent, then funnels the prompt through AgentDispatch.
public abstract class AgentCommand : Command
{
    protected override string CommandContextHelpUrl => "https://mcneel.github.io/RhinoMCP";

    // The agent name this command targets; SetActive before dispatch so the named command is meaningful.
    private protected abstract string AgentName { get; }

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // NOTE : On Rhino 8 Mac Get Literal String doesn't work so idk
        GetString get = new();
        get.SetCommandPrompt(EnglishName);

        if (get.GetLiteralString() != Rhino.Input.GetResult.String) return Result.Cancel;
        string request = get.StringResult();
        if (string.IsNullOrWhiteSpace(request)) return Result.Cancel;

        AgentHost.SetActive(doc, AgentName);
        AgentDispatch.PromptActive(doc, UserMessage.FromText(request));
        return Result.Success;
    }
}
