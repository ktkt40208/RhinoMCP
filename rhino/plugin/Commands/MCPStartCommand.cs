using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhMcp;

public class MCPStartCommand : Command
{

    public override string EnglishName => "MCPStart";

    protected override string CommandContextHelpUrl => "https://mcneel.github.io/RhinoMCP";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        GetInteger go = new ();
        go.SetCommandPrompt("MCPStart Port");
        go.AcceptNothing(true);
        go.AcceptEnterWhenDone(true);
        if (RhinoMcpHost.TryGetNextPort(out int suggestedPort))
            go.SetDefaultInteger(suggestedPort);
        go.SetLowerLimit(1, false);
        go.SetUpperLimit(65535, false);
        if (go.Get() != GetResult.Number) return Result.Cancel;
        int port = go.Number();

        return RhinoMcpHost.StartOrRestart(doc, port) ? Result.Success : Result.Failure;
    }
}
