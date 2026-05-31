using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoDoc.BeginOpenDocument += Register;
        CommandInterceptor.Attach();
        return base.OnLoad(ref errorMessage);
    }

    protected override void OnShutdown()
    {
        CommandInterceptor.Detach();
    }

    private void Register(object? sender, DocumentOpenEventArgs e)
    {
        RhinoDoc.BeginOpenDocument -= Register;

        string? portStr = Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar);
        if (!string.IsNullOrEmpty(portStr)) return;

        try
        {
            int port = RhinoMcpHost.GetNextPort();
            if (RhinoMcpHost.StartOrRestart(e.Document, port, true))
            {
                RhinoApp.WriteLine("The Rhino MCP Platform is ready.");
                return;
            }
        }
        catch
        {
        }
        
        RhinoApp.WriteLine("The Rhino MCP Server failed to start");
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}
