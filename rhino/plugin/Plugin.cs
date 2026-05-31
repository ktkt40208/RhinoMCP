using System.Drawing;
using System.IO;
using System.Reflection;
using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{
    private const string IconResourceName = "RhMcp.logo.svg";

    private CommandInterceptorHost? CommandInterceptors { get; set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoDoc.BeginOpenDocument += Register;
        CommandInterceptors = new CommandInterceptorHost();

        // Probe agent install paths once on load so the active agent resolves before the first
        // prompt; Part 1's settings dialog re-runs this when the agent config changes.
        AgentRegistry.Refresh();

        Rhino.UI.Panels.RegisterPanel(this, typeof(RhMcpPanel), "AI", LoadPanelIcon(), Rhino.UI.PanelType.PerDoc);
        return base.OnLoad(ref errorMessage);
    }

    // GetHicon isn't guaranteed on every platform, so fall back to no icon rather than fail OnLoad.
    private static System.Drawing.Icon? LoadPanelIcon()
    {
        try
        {
            Assembly assembly = typeof(RhMcpPlugin).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(IconResourceName);
            if (stream is null)
                return null;

            using StreamReader reader = new(stream);
            string svg = reader.ReadToEnd();

            Size size = Rhino.UI.Panels.IconSizeInPixels;
            int pixels = size.Width > 0 ? size.Width : 36;
            using Bitmap bitmap = Rhino.UI.DrawingUtilities.BitmapFromSvg(svg, pixels, pixels, adjustForDarkMode: true);
            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
            return null;
        }
    }

    protected override void OnShutdown()
    {
        CommandInterceptors?.Dispose();
        AgentHost.Shutdown();
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
