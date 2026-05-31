using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoCommand = Rhino.Commands.Command;

namespace RhMcp;

[Guid("fb948c98-5987-45a3-8dcb-2814ed77ee3b")]
public class RhMcpPanel : Panel
{
    public static Guid PanelId => typeof(RhMcpPanel).GUID;

    public RhMcpPanel()
    {
        Padding = new Padding(12);

        Label title = new()
        {
            Text = "Rhino MCP Platform",
            Font = SystemFonts.Bold(),
        };

        Label blurb = new()
        {
            Text = "MCP server status and controls.",
            Wrap = WrapMode.Word,
        };

        Content = new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(title),
                new TableRow(blurb),
                null,
            },
        };
    }
}

public class MCPPanelCommand : RhinoCommand
{
    public override string EnglishName => "MCPPanel";

    protected override string CommandContextHelpUrl => "https://mcneel.github.io/RhinoMCP";

    protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, Rhino.Commands.RunMode mode)
    {
        Guid panelId = RhMcpPanel.PanelId;
        bool visible = Rhino.UI.Panels.IsPanelVisible(panelId);
        if (visible)
            Rhino.UI.Panels.ClosePanel(panelId);
        else
            Rhino.UI.Panels.OpenPanel(panelId);
        return Rhino.Commands.Result.Success;
    }
}
