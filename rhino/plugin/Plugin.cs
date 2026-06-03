using System.Threading;
using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{

    // Cancelled on shutdown so any startup background work stops cleanly with Rhino.
    private CancellationTokenSource Shutdown { get; } = new();

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoDoc.BeginOpenDocument += Register;

        // Wire the bundled rhino MCP server into any MCP-aware tools the user already has, so
        // external agents work out of the box. Background: never block or fail OnLoad.
        McpClientConfigInstaller.InstallInBackground(Shutdown.Token);

        return base.OnLoad(ref errorMessage);
    }

    protected override void OnShutdown()
    {
        Shutdown.Cancel();
        Shutdown.Dispose();
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
