using System.Collections.ObjectModel;
using System.Reflection;
using Eto.Drawing;
using Eto.Forms;

namespace RhMcp;

internal sealed class AISettingsDialog : Dialog
{
    private ObservableCollection<AgentRow> Rows { get; } = [];
    private GridView AgentGrid { get; } = new() { ShowHeader = true, AllowMultipleSelection = false };

    private TextArea SearchPathsBox { get; } = new() { Wrap = false, Height = 70 };
    private TextBox ModelBox { get; } = new();
    private TextArea ExtraArgsBox { get; } = new() { Wrap = false, Height = 50 };
    private TextArea SystemPromptBox { get; } = new() { Wrap = true, Height = 70 };

    private Button RemoveButton { get; } = new() { Text = "Remove" };
    private Button UpButton { get; } = new() { Text = "Up" };
    private Button DownButton { get; } = new() { Text = "Down" };
    private Button SetDefaultButton { get; } = new() { Text = "Set Default" };

    private TextArea McpJsonBox { get; } = new() { Wrap = false, Font = Fonts.Monospace(11) };
    private Label McpErrorLabel { get; } = new() { TextColor = Colors.Red, Visible = false };

    // Leaf tool rows in the Tools tree, kept flat so Commit can read each checkbox back
    // without re-walking the grouped tree.
    private List<ToolNode> ToolLeaves { get; } = [];

    private static JsonSerializerOptions IndentedJson { get; } = new() { WriteIndented = true };
    private const string EmptyMcpJson = "{\n  \"mcpServers\": {}\n}";

    // Suppresses the editor->row write-back while we are programmatically loading
    // the editor from a freshly selected row.
    private bool Loading { get; set; }

    public AISettingsDialog()
    {
        Title = "AI Settings";
        Padding = new Padding(12);
        Size = new Size(720, 680);
        MinimumSize = new Size(560, 440);
        Resizable = true;

        SeedRows();

        TabControl tabs = new();
        tabs.Pages.Add(new TabPage { Text = "AI Agents", Content = AgentsTab() });
        tabs.Pages.Add(new TabPage { Text = "MCP Servers", Content = McpServersTab() });
        tabs.Pages.Add(new TabPage { Text = "Tools", Content = ToolsTab() });

        Button saveButton = new() { Text = "Save" };
        saveButton.Click += (_, _) => Commit();

        Button closeButton = new() { Text = "Cancel" };
        closeButton.Click += (_, _) => Close();

        StackLayout buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Items = { null, closeButton, saveButton },
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

        DefaultButton = saveButton;
        AbortButton = closeButton;
    }

    private void SeedRows()
    {
        AgentRegistry.Refresh();
        Dictionary<string, bool> available = AgentRegistry.Chain
            .ToDictionary(r => r.Definition.Name, r => r.Available, StringComparer.OrdinalIgnoreCase);

        string defaultName = AISettings.DefaultAgentName;
        bool anyDefault = false;
        foreach (AgentDefinition def in AISettings.GetAgents())
        {
            bool isDefault = string.Equals(def.Name, defaultName, StringComparison.OrdinalIgnoreCase);
            anyDefault |= isDefault;
            Rows.Add(AgentRow.From(def, available.GetValueOrDefault(def.Name, false), isDefault));
        }

        if (!anyDefault && Rows.Count > 0)
            Rows[0].IsDefault = true;
    }

    private Control AgentsTab()
    {
        AgentGrid.DataStore = Rows;
        AgentGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Status",
            DataCell = new TextBoxCell { Binding = Binding.Property((AgentRow r) => r.StatusGlyph) },
            Editable = false,
            Width = 56,
        });
        AgentGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Default",
            DataCell = new TextBoxCell { Binding = Binding.Property((AgentRow r) => r.DefaultGlyph) },
            Editable = false,
            Width = 56,
        });
        AgentGrid.Columns.Add(new GridColumn
        {
            HeaderText = "On",
            DataCell = new CheckBoxCell
            {
                Binding = Binding.Delegate<AgentRow, bool?>(r => r.Enabled, (r, v) => r.Enabled = v == true),
            },
            Editable = true,
            Width = 44,
        });
        AgentGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Name",
            DataCell = new TextBoxCell { Binding = Binding.Property((AgentRow r) => r.Name) },
            Editable = false,
            Width = 160,
        });
        AgentGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Based on",
            DataCell = new TextBoxCell { Binding = Binding.Property((AgentRow r) => r.Adapter.ToString()) },
            Editable = false,
            Width = 90,
        });

        AgentGrid.SelectionChanged += (_, _) => LoadEditor();

        Button addButton = new() { Text = "Add Custom..." };
        addButton.Click += (_, _) => AddCustom();
        RemoveButton.Click += (_, _) => RemoveSelected();
        UpButton.Click += (_, _) => MoveSelected(-1);
        DownButton.Click += (_, _) => MoveSelected(1);
        SetDefaultButton.Click += (_, _) => SetSelectedDefault();

        StackLayout rowButtons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Items = { addButton, RemoveButton, UpButton, DownButton, SetDefaultButton },
        };

        SearchPathsBox.TextChanged += (_, _) => WriteEditor(row => row.SearchPathsText = SearchPathsBox.Text);
        ModelBox.TextChanged += (_, _) => WriteEditor(row => row.Model = ModelBox.Text);
        ExtraArgsBox.TextChanged += (_, _) => WriteEditor(row => row.ExtraArgsText = ExtraArgsBox.Text);
        SystemPromptBox.TextChanged += (_, _) => WriteEditor(row => row.SystemPrompt = SystemPromptBox.Text);

        TableLayout editor = new()
        {
            Spacing = new Size(8, 6),
            Rows =
            {
                LabeledRow("Search paths (one per line):", SearchPathsBox),
                LabeledRow("Model:", ModelBox),
                LabeledRow("Extra args (one per line):", ExtraArgsBox),
                LabeledRow("Default prompt:", SystemPromptBox),
            },
        };

        TableLayout layout = new()
        {
            Padding = new Padding(8),
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(AgentGrid) { ScaleHeight = true },
                new TableRow(rowButtons),
                new TableRow(new Label
                {
                    Text = "Built-ins (claude, codex) are editable but not removable.",
                    TextColor = Colors.Gray,
                }),
                new TableRow(editor),
            },
        };

        LoadEditor();
        return layout;
    }

    private static TableRow LabeledRow(string label, Control control) =>
        new(new TableCell(new Label { Text = label }), new TableCell(control, true));

    private void LoadEditor()
    {
        Loading = true;
        try
        {
            if (TryGetSelected(out AgentRow row))
            {
                SearchPathsBox.Text = row.SearchPathsText;
                ModelBox.Text = row.Model;
                ExtraArgsBox.Text = row.ExtraArgsText;
                SystemPromptBox.Text = row.SystemPrompt;
                EnableEditor(true);
                RemoveButton.Enabled = !row.IsBuiltin;
            }
            else
            {
                SearchPathsBox.Text = string.Empty;
                ModelBox.Text = string.Empty;
                ExtraArgsBox.Text = string.Empty;
                SystemPromptBox.Text = string.Empty;
                EnableEditor(false);
                RemoveButton.Enabled = false;
            }
        }
        finally { Loading = false; }
    }

    private void EnableEditor(bool enabled)
    {
        SearchPathsBox.Enabled = enabled;
        ModelBox.Enabled = enabled;
        ExtraArgsBox.Enabled = enabled;
        SystemPromptBox.Enabled = enabled;
        UpButton.Enabled = enabled;
        DownButton.Enabled = enabled;
        SetDefaultButton.Enabled = enabled;
    }

    private void WriteEditor(Action<AgentRow> apply)
    {
        if (Loading)
            return;
        if (TryGetSelected(out AgentRow row))
            apply(row);
    }

    private bool TryGetSelected(out AgentRow row)
    {
        if (AgentGrid.SelectedItem is AgentRow selected)
        {
            row = selected;
            return true;
        }
        row = default!;
        return false;
    }

    private void SetSelectedDefault()
    {
        if (!TryGetSelected(out AgentRow row))
            return;
        foreach (AgentRow other in Rows)
            other.IsDefault = false;
        row.IsDefault = true;
        ReloadGrid();
    }

    private void ReloadGrid()
    {
        if (Rows.Count == 0)
            return;
        AgentGrid.ReloadData(new Eto.Forms.Range<int>(0, Rows.Count - 1));
    }

    private void MoveSelected(int delta)
    {
        if (!TryGetSelected(out AgentRow row))
            return;
        int index = Rows.IndexOf(row);
        int target = index + delta;
        if (target < 0 || target >= Rows.Count)
            return;
        Rows.Move(index, target);
        AgentGrid.SelectedRow = target;
    }

    private void AddCustom()
    {
        if (!TryPromptCustom(out string name, out AgentAdapter adapter))
            return;

        if (Rows.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"An agent named \"{name}\" already exists.", "Add Custom Agent", MessageBoxButtons.OK, MessageBoxType.Warning);
            return;
        }

        string command = adapter switch
        {
            AgentAdapter.Claude => "claude",
            AgentAdapter.Codex => "codex",
            _ => name,
        };

        AgentRow row = new(
            name: name,
            adapter: adapter,
            command: command,
            searchPathsText: string.Join(Environment.NewLine, AgentRegistry.DefaultSearchPaths(command)),
            model: string.Empty,
            extraArgsText: string.Empty,
            systemPrompt: string.Empty,
            enabled: true,
            isBuiltin: false,
            isDefault: false,
            available: false);

        Rows.Add(row);
        AgentGrid.SelectedRow = Rows.Count - 1;
    }

    private bool TryPromptCustom(out string name, out AgentAdapter adapter)
    {
        name = string.Empty;
        adapter = AgentAdapter.Claude;

        TextBox nameBox = new();
        DropDown adapterBox = new();
        foreach (AgentAdapter value in Enum.GetValues<AgentAdapter>())
            adapterBox.Items.Add(value.ToString());
        adapterBox.SelectedIndex = 0;

        Dialog<bool> prompt = new()
        {
            Title = "Add Custom Agent",
            Padding = new Padding(12),
            MinimumSize = new Size(320, 0),
        };

        Button ok = new() { Text = "Add" };
        ok.Click += (_, _) => prompt.Close(true);
        Button cancel = new() { Text = "Cancel" };
        cancel.Click += (_, _) => prompt.Close(false);

        prompt.Content = new TableLayout
        {
            Spacing = new Size(8, 6),
            Rows =
            {
                LabeledRow("Name:", nameBox),
                LabeledRow("Based on:", adapterBox),
                new TableRow(new TableCell(), new TableCell(new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items = { null, cancel, ok },
                })),
            },
        };
        prompt.DefaultButton = ok;
        prompt.AbortButton = cancel;

        if (!prompt.ShowModal(this))
            return false;

        string entered = nameBox.Text?.Trim() ?? string.Empty;
        if (entered.Length == 0)
            return false;

        name = entered;
        adapter = Enum.GetValues<AgentAdapter>()[adapterBox.SelectedIndex];
        return true;
    }

    private void RemoveSelected()
    {
        if (!TryGetSelected(out AgentRow row) || row.IsBuiltin)
            return;
        Rows.Remove(row);
        LoadEditor();
    }

    private Control McpServersTab()
    {
        McpJsonBox.Text = PrettyJson(AISettings.ExtraMcpServersJson);

        Label help = new()
        {
            Wrap = WrapMode.Word,
            Text = "Extra MCP servers merged into every agent alongside the built-in \"rhino\" server.",
            TextColor = Colors.Gray,
        };

        return new TableLayout
        {
            Padding = new Padding(8),
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(help),
                new TableRow(McpJsonBox) { ScaleHeight = true },
                new TableRow(McpErrorLabel),
            },
        };
    }

    private Control ToolsTab()
    {
        HashSet<string> disabled = new(AISettings.DisabledTools, StringComparer.OrdinalIgnoreCase);

        TreeGridItemCollection roots = [];
        foreach (IGrouping<string, ToolInfo> group in ScanTools()
                     .GroupBy(t => t.Category)
                     .OrderBy(g => CategoryOrder(g.Key)))
        {
            ToolNode groupNode = new(group.Key) { Expanded = true };
            foreach (ToolInfo tool in group.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
            {
                ToolNode leaf = new(tool.Name, !disabled.Contains(tool.Name), tool.Title, tool.Description)
                {
                    Parent = groupNode,
                };
                groupNode.Children.Add(leaf);
                ToolLeaves.Add(leaf);
            }
            SyncGroupState(groupNode);
            roots.Add(groupNode);
        }

        TreeGridView tree = new() { ShowHeader = true, DataStore = roots };
        tree.Columns.Add(new GridColumn { HeaderText = "On", DataCell = new CheckBoxCell(0), Editable = true, Width = 44 });
        tree.Columns.Add(new GridColumn { HeaderText = "Tool", DataCell = new TextBoxCell(1), Width = 210 });
        tree.Columns.Add(new GridColumn { HeaderText = "Description", DataCell = new TextBoxCell(2), Width = 380 });
        tree.CellEdited += (_, e) =>
        {
            if (e.Column != 0 || e.Item is not ToolNode node)
                return;
            if (node.IsGroup)
            {
                bool on = node.GetValue(0) is true;
                foreach (ToolNode child in node.Children.OfType<ToolNode>())
                    child.SetValue(0, on);
                tree.ReloadItem(node);
            }
            else if (node.Parent is ToolNode group)
            {
                SyncGroupState(group);
                tree.ReloadItem(group);
            }
        };

        Label help = new()
        {
            Wrap = WrapMode.Word,
            Text = "Tools the built-in \"rhino\" server exposes, grouped by behaviour. Unchecking a tool "
                + "hides it from in-Rhino agents only; external clients still see every tool.",
            TextColor = Colors.Gray,
        };

        return new TableLayout
        {
            Padding = new Padding(8),
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(help),
                new TableRow(tree) { ScaleHeight = true },
            },
        };
    }

    private static int CategoryOrder(string category) => category switch
    {
        "Read-only" => 0,
        "Modify" => 1,
        "Destructive" => 2,
        _ => 3,
    };

    // A group checkbox is checked only when every tool under it is on; toggling it cascades to all
    // children. Mixed groups read as unchecked (no tri-state) to keep the model free of nulls.
    private static void SyncGroupState(ToolNode group)
    {
        List<ToolNode> children = group.Children.OfType<ToolNode>().ToList();
        bool allOn = children.Count > 0 && children.All(c => c.GetValue(0) is true);
        group.SetValue(0, allOn);
    }

    // Mirror of ToolRegistry.Scan that reads name/title/description/behaviour without instantiating
    // tools or needing an IServiceProvider (Scan does the latter to build full schemas, which we must
    // not do here). Router-internal tools (leading underscore) are excluded so they can't be hidden.
    private static IReadOnlyList<ToolInfo> ScanTools()
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        List<ToolInfo> tools = [];
        Assembly assembly = typeof(McpSerializer).Assembly;
        foreach (Type type in SafeGetTypes(assembly))
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not McpServerToolAttribute toolAttr)
                    continue;

                string name = toolAttr.Name ?? method.Name;
                if (name.StartsWith('_'))
                    continue;

                string title = string.IsNullOrWhiteSpace(toolAttr.Title) ? name : toolAttr.Title!;
                string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                string category = toolAttr.ReadOnly ? "Read-only" : toolAttr.Destructive ? "Destructive" : "Modify";
                tools.Add(new ToolInfo(name, title, description, category));
            }
        }

        return tools;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private void Commit()
    {
        if (!TryValidateMcpJson(McpJsonBox.Text, out string normalizedJson, out string error))
        {
            McpErrorLabel.Text = error;
            McpErrorLabel.Visible = true;
            return;
        }
        McpErrorLabel.Visible = false;

        AgentDefinition[] definitions = Rows.Select(ToDefinition).ToArray();
        AISettings.SetAgents(definitions);

        AgentRow defaultRow = Rows.FirstOrDefault(r => r.IsDefault) ?? Rows.First();
        AISettings.DefaultAgentName = defaultRow.Name;

        AISettings.ExtraMcpServersJson = normalizedJson;

        // ScanTools hides router-internal underscore tools from the grid, so they have no checkbox to
        // round-trip; carry forward any that were already disabled instead of silently dropping them.
        IEnumerable<string> uncheckedNames = ToolLeaves
            .Where(leaf => leaf.GetValue(0) is not true)
            .Select(leaf => leaf.ToolName);
        IEnumerable<string> preservedUnderscore = AISettings.DisabledTools
            .Where(n => n.StartsWith('_'));
        AISettings.DisabledTools = uncheckedNames
            .Concat(preservedUnderscore)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AgentRegistry.Refresh();
        Close();
    }

    private static AgentDefinition ToDefinition(AgentRow row) =>
        new(
            row.Name,
            row.Adapter,
            row.Command,
            SplitLines(row.SearchPathsText),
            row.Model.Trim(),
            SplitLines(row.ExtraArgsText),
            row.SystemPrompt,
            row.Enabled,
            row.IsBuiltin);

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private static string PrettyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return EmptyMcpJson;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, IndentedJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static bool TryValidateMcpJson(string json, out string normalized, out string error)
    {
        normalized = EmptyMcpJson;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "MCP config must be a JSON object.";
                return false;
            }
            if (!doc.RootElement.TryGetProperty("mcpServers", out JsonElement servers)
                || servers.ValueKind != JsonValueKind.Object)
            {
                error = "MCP config must contain an \"mcpServers\" object.";
                return false;
            }
            normalized = JsonSerializer.Serialize(doc.RootElement, IndentedJson);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    // Immutable scan result for one tool row in the Tools tree.
    private readonly record struct ToolInfo(string Name, string Title, string Description, string Category);

    // TreeGridView node for the Tools tab. Group nodes carry the category label in column 1 and a
    // roll-up checkbox in column 0; leaf nodes carry [enabled, title, description] and the tool name.
    private sealed class ToolNode : TreeGridItem
    {
        public string ToolName { get; }
        public bool IsGroup { get; }

        public ToolNode(string toolName, bool enabled, string title, string description)
            : base(enabled, title, description)
        {
            ToolName = toolName;
            IsGroup = false;
        }

        public ToolNode(string category)
            : base(false, category, string.Empty)
        {
            ToolName = string.Empty;
            IsGroup = true;
        }
    }

    // Mutable view-model backing the agent grid + editor. Converted to/from the immutable
    // AgentDefinition record at the dialog boundary; IsBuiltin and Adapter are carried through
    // unchanged so editing a built-in never silently turns it into a custom entry.
    private sealed class AgentRow
    {
        public string Name { get; }
        public AgentAdapter Adapter { get; }
        public string Command { get; }
        public bool IsBuiltin { get; }
        public bool Available { get; }

        public string SearchPathsText { get; set; }
        public string Model { get; set; }
        public string ExtraArgsText { get; set; }
        public string SystemPrompt { get; set; }
        public bool Enabled { get; set; }
        public bool IsDefault { get; set; }

        public string StatusGlyph => Available ? "✓" : "✗";
        public string DefaultGlyph => IsDefault ? "★" : string.Empty;

        public AgentRow(
            string name, AgentAdapter adapter, string command, string searchPathsText, string model,
            string extraArgsText, string systemPrompt, bool enabled, bool isBuiltin, bool isDefault, bool available)
        {
            Name = name;
            Adapter = adapter;
            Command = command;
            SearchPathsText = searchPathsText;
            Model = model;
            ExtraArgsText = extraArgsText;
            SystemPrompt = systemPrompt;
            Enabled = enabled;
            IsBuiltin = isBuiltin;
            IsDefault = isDefault;
            Available = available;
        }

        public static AgentRow From(AgentDefinition def, bool available, bool isDefault) =>
            new(
                def.Name,
                def.Adapter,
                def.Command,
                string.Join(Environment.NewLine, def.SearchPaths),
                def.Model,
                string.Join(Environment.NewLine, def.ExtraArgs),
                def.SystemPrompt,
                def.Enabled,
                def.IsBuiltin,
                isDefault,
                available);
    }
}
