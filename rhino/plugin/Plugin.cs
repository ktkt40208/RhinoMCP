using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoDoc.BeginOpenDocument += Register;
        return base.OnLoad(ref errorMessage);
    }

    private void Register(object? sender, DocumentOpenEventArgs e)
    {
        RhinoDoc.BeginOpenDocument -= Register;

        try
        {
            int port = RhinoMcpHost.GetNextPort();
            if (RhinoMcpHost.StartOrRestart(e.Document, port, true))
            {
                RhinoApp.WriteLine("The Rhino MCP Platform is ready.");
            }
        }
        catch
        {
            RhinoApp.WriteLine("The Rhino MCP Server failed to start");
        }
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}
