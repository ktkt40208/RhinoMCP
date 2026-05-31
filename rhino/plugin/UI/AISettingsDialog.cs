using Eto.Drawing;
using Eto.Forms;

namespace RhMcp;

internal sealed class AISettingsDialog : Dialog
{
    public AISettingsDialog()
    {
        Title = "AI Settings";
        Padding = new Padding(12);
        Size = new Size(560, 420);
        MinimumSize = new Size(400, 300);

        TabControl tabs = new();
        tabs.Pages.Add(new TabPage { Text = "MCP Servers", Content = McpServersTab() });
        tabs.Pages.Add(new TabPage { Text = "AI Agents", Content = AgentsTab() });
        tabs.Pages.Add(new TabPage { Text = "Default Agent", Content = DefaultAgentTab() });

        Button closeButton = new() { Text = "Close" };
        closeButton.Click += (_, _) => Close();

        StackLayout buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Items = { null, closeButton },
        };

        Content = new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(tabs) { ScaleHeight = true },
                new TableRow(buttons),
            },
        };

        DefaultButton = closeButton;
        AbortButton = closeButton;
    }

    private static Control McpServersTab() => new Panel { Padding = new Padding(8) };

    private static Control AgentsTab() => new Panel { Padding = new Padding(8) };

    private static Control DefaultAgentTab() => new Panel { Padding = new Padding(8) };
}
