using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoCommand = Rhino.Commands.Command;

namespace RhMcp;

// One panel instance per document (PanelType.PerDoc). Renders the active agent's Conversation as
// chat bubbles + tool chips, sends prompts through the shared AgentDispatch funnel, and re-renders
// off the Conversation.Changed event marshaled onto the UI thread (the reader loop is off-thread).
[Guid("fb948c98-5987-45a3-8dcb-2814ed77ee3b")]
public class RhMcpPanel : Panel
{
    public static Guid PanelId => typeof(RhMcpPanel).GUID;

    private uint DocSerial { get; }

    private DropDown AgentPicker { get; } = new();
    private Label ModelLabel { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
    private DropDown RecentPicker { get; } = new() { ToolTip = "Previous conversations" };
    private TextArea PromptBox { get; } = new() { AcceptsReturn = true, AcceptsTab = false, Height = 64 };
    private Button SendButton { get; } = new() { Text = "Send" };
    private StackLayout TranscriptStack { get; } = new() { Spacing = 8, Padding = new Padding(4) };
    private StackLayout AttachmentStrip { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private Scrollable TranscriptScroll { get; } = new() { ExpandContentWidth = true };

    private List<Attachment> Pending { get; } = new();

    // Set by Part 6 to persist a conversation before "New conversation" drops it, and to feed the
    // Prev Convos picker. Default no-ops so the panel works standalone.
    internal Action<Conversation> PersistConversationHook { get; set; } = static _ => { };
    internal Func<IReadOnlyList<string>> RecentConversationsHook { get; set; } = static () => [];

    // Resubscribed on every agent switch; tracked so we can unhook the old one.
    private Conversation? Subscribed { get; set; }
    private Action? SubscribedHandler { get; set; }

    // Suppresses OnAgentPicked while the dropdown is repopulated programmatically, so only a
    // genuine user pick drives SetActive/Resubscribe/Rerender.
    private bool Populating { get; set; }

    public RhMcpPanel()
        : this(Rhino.RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u)
    {
    }

    public RhMcpPanel(uint documentSerialNumber)
    {
        DocSerial = documentSerialNumber;

        Padding = new Padding(8);
        TranscriptScroll.Content = TranscriptStack;

        AgentPicker.SelectedValueChanged += OnAgentPicked;
        SendButton.Click += (_, _) => OnSendOrStop();

        Button settingsGear = new() { Text = "⚙", ToolTip = "AI Settings", Width = 32 };
        settingsGear.Click += (_, _) => OpenSettings();

        Button newConvo = new() { Text = "New", ToolTip = "New conversation" };
        newConvo.Click += (_, _) => OnNewConversation();

        Button attachButton = new() { Text = "+", ToolTip = "Attach a file", Width = 32 };
        attachButton.Click += (_, _) => OnPickFile();

        PromptBox.KeyDown += OnPromptKeyDown;

        StackLayout header = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                settingsGear,
                new StackLayoutItem(AgentPicker, true),
                ModelLabel,
                RecentPicker,
                newConvo,
            },
        };

        StackLayout promptRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Items =
            {
                attachButton,
                new StackLayoutItem(PromptBox, true),
                SendButton,
            },
        };

        Content = new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(header),
                new TableRow(TranscriptScroll) { ScaleHeight = true },
                new TableRow(AttachmentStrip),
                new TableRow(promptRow),
            },
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reload();
    }

    // The Conversation is owned by the pooled CliAgent in AgentHost and outlives this panel, so
    // its off-thread Changed event would otherwise keep firing into a disposed panel (AppKit abort
    // / leak). Detach on unload; Loaded then gates any Rerender already queued via InvokeOnUiThread.
    protected override void OnUnLoad(EventArgs e)
    {
        if (Subscribed is not null && SubscribedHandler is not null)
            Subscribed.Changed -= SubscribedHandler;
        Subscribed = null;
        SubscribedHandler = null;
        base.OnUnLoad(e);
    }

    private bool TryDoc(out RhinoDoc doc)
    {
        doc = RhinoDoc.FromRuntimeSerialNumber(DocSerial) ?? RhinoDoc.ActiveDoc;
        return doc is not null;
    }

    private void Reload()
    {
        PopulateAgents();
        PopulateRecents();
        Resubscribe();
        Rerender();
    }

    private void PopulateAgents()
    {
        Populating = true;
        try
        {
            AgentPicker.Items.Clear();
            IReadOnlyList<ResolvedAgent> chain = AgentRegistry.Chain;
            foreach (ResolvedAgent r in chain)
            {
                string label = r.Available ? r.Definition.Name : $"{r.Definition.Name} (not found)";
                AgentPicker.Items.Add(new ListItem { Text = label, Key = r.Definition.Name });
            }

            // Reflect the resolved active agent without pinning: only a genuine user pick should
            // pin via AgentHost.SetActive, so the registry's first-Enabled-and-Available fallback
            // stays authoritative as availability changes.
            if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgent active))
                AgentPicker.SelectedKey = active.Name;
            else if (chain.FirstOrDefault(static r => r.Available) is { } available)
                AgentPicker.SelectedKey = available.Definition.Name;
            else if (AgentPicker.Items.Count > 0)
                AgentPicker.SelectedIndex = 0;
        }
        finally
        {
            Populating = false;
        }

        UpdateModelLabel();
    }

    private void UpdateModelLabel()
    {
        string key = AgentPicker.SelectedKey ?? string.Empty;
        ResolvedAgent? match = AgentRegistry.Chain.FirstOrDefault(r => r.Definition.Name == key);
        string model = match is { Definition.Model: { Length: > 0 } m } ? m : "default";
        ModelLabel.Text = $"model: {model}";
    }

    private void PopulateRecents()
    {
        RecentPicker.Items.Clear();
        RecentPicker.Items.Add(new ListItem { Text = "Prev Convos", Key = string.Empty });
        foreach (string label in RecentConversationsHook())
            RecentPicker.Items.Add(new ListItem { Text = label, Key = label });
        RecentPicker.SelectedIndex = 0;
    }

    private void OnAgentPicked(object? sender, EventArgs e)
    {
        if (Populating)
            return;

        string? name = AgentPicker.SelectedKey;
        if (string.IsNullOrEmpty(name) || !TryDoc(out RhinoDoc doc))
            return;
        AgentHost.SetActive(doc, name);
        UpdateModelLabel();
        Resubscribe();
        Rerender();
    }

    // Subscribe to the active agent's Conversation so writes from the off-thread reader loop
    // re-render the transcript; unhook the previous one first. No agent yet -> nothing to hook.
    private void Resubscribe()
    {
        if (Subscribed is not null && SubscribedHandler is not null)
            Subscribed.Changed -= SubscribedHandler;
        Subscribed = null;
        SubscribedHandler = null;

        if (!TryActiveConversation(out Conversation convo))
            return;

        Action handler = OnConversationChanged;
        convo.Changed += handler;
        Subscribed = convo;
        SubscribedHandler = handler;
    }

    private bool TryActiveConversation(out Conversation convo)
    {
        if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgent agent) && agent is CliAgent cli)
        {
            convo = cli.Conversation;
            return true;
        }
        convo = default!;
        return false;
    }

    // Off-thread (reader loop). Eto is UI-thread-only, so marshal the rebuild.
    private void OnConversationChanged() => RhinoApp.InvokeOnUiThread(new Action(Rerender));

    private void Rerender()
    {
        // A Rerender queued onto the UI thread can land after the panel is unloaded; mutating
        // detached Eto controls risks an AppKit abort / ObjectDisposedException.
        if (!Loaded)
            return;

        TranscriptStack.Items.Clear();

        bool anyAgent = AgentRegistry.Chain.Any(static r => r.Available);
        if (!anyAgent)
        {
            ShowNoAgentState();
            SyncSendButton(false);
            return;
        }

        if (!TryActiveConversation(out Conversation convo))
        {
            TranscriptStack.Items.Add(Bubble("Start a conversation with the active agent.", false));
            SyncSendButton(false);
            return;
        }

        foreach (TurnEvent ev in convo.Lifecycle)
            TranscriptStack.Items.Add(SystemLine(ev.Text));

        bool running = false;
        foreach (Turn turn in convo.Turns)
        {
            TranscriptStack.Items.Add(Bubble(turn.Prompt, true));
            AppendTurnEvents(turn);
            running = !turn.Completed;
        }

        SyncSendButton(running);
        ScrollToBottom();
    }

    private void AppendTurnEvents(Turn turn)
    {
        System.Text.StringBuilder assistant = new();
        void FlushAssistant()
        {
            if (assistant.Length == 0)
                return;
            TranscriptStack.Items.Add(Bubble(assistant.ToString(), false));
            assistant.Clear();
        }

        foreach (TurnEvent ev in turn.Events)
        {
            switch (ev.Kind)
            {
                case TurnEventKind.AssistantText:
                    assistant.Append(ev.Text);
                    break;
                case TurnEventKind.ToolUse:
                    FlushAssistant();
                    TranscriptStack.Items.Add(ToolChip(ev));
                    break;
                case TurnEventKind.Result:
                    FlushAssistant();
                    if (!string.IsNullOrWhiteSpace(ev.Text))
                        TranscriptStack.Items.Add(Bubble(ev.Text, false));
                    break;
            }
        }
        FlushAssistant();
    }

    private static Control SystemLine(string text) => new Label
    {
        Text = $"— {text} —",
        TextAlignment = TextAlignment.Center,
        TextColor = SystemColors.DisabledText,
        Font = SystemFonts.Default(7),
    };

    private static Control Bubble(string text, bool user)
    {
        Label body = new() { Text = text, Wrap = WrapMode.Word };
        Panel inner = new()
        {
            Padding = new Padding(8, 6),
            BackgroundColor = user ? Color.FromArgb(0x33, 0x66, 0xCC) : SystemColors.ControlBackground,
            Content = body,
        };
        if (user)
            body.TextColor = Colors.White;

        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Items =
            {
                user ? new StackLayoutItem(null, true) : new StackLayoutItem(inner),
                user ? new StackLayoutItem(inner) : new StackLayoutItem(null, true),
            },
        };
    }

    // Compact one-line chip; click toggles an expander showing the tool's args + result.
    private static Control ToolChip(TurnEvent ev)
    {
        string detail = BuildToolDetail(ev);
        Label headerLabel = new()
        {
            Text = $"⚙ {ev.Text}",
            Font = SystemFonts.Default(8),
            TextColor = SystemColors.ControlText,
        };

        if (detail.Length == 0)
            return new Panel { Padding = new Padding(6, 3), Content = headerLabel };

        Expander expander = new()
        {
            Header = headerLabel,
            Expanded = false,
            Content = new Label
            {
                Text = detail,
                Wrap = WrapMode.Word,
                Font = SystemFonts.Default(8),
                TextColor = SystemColors.DisabledText,
            },
        };
        return new Panel { Padding = new Padding(6, 3), Content = expander };
    }

    private static string BuildToolDetail(TurnEvent ev)
    {
        System.Text.StringBuilder sb = new();
        if (!string.IsNullOrWhiteSpace(ev.Args))
            sb.Append("args: ").Append(ev.Args);
        if (!string.IsNullOrWhiteSpace(ev.Result))
        {
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append("result: ").Append(ev.Result);
        }
        return sb.ToString();
    }

    private void ShowNoAgentState()
    {
        Button open = new() { Text = "Open AI Settings" };
        open.Click += (_, _) => OpenSettings();
        TranscriptStack.Items.Add(new StackLayout
        {
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items =
            {
                new Label { Text = "No AI agent found.", Font = SystemFonts.Bold() },
                new Label
                {
                    Text = "Install Claude or Codex, or configure a search path in AI Settings.",
                    Wrap = WrapMode.Word,
                    TextAlignment = TextAlignment.Center,
                },
                open,
            },
        });
    }

    private void SyncSendButton(bool running)
    {
        SendButton.Text = running ? "Stop" : "Send";
        SendButton.ToolTip = running ? "Cancel the current turn" : "Send (Enter)";
    }

    // Eto layout is deferred, so TranscriptStack's size is stale right after a rebuild; defer the
    // scroll until after layout settles so streaming actually reaches the true bottom.
    private void ScrollToBottom() => Application.Instance.AsyncInvoke(() =>
    {
        if (Loaded)
            TranscriptScroll.ScrollPosition = new Point(0, TranscriptStack.Height);
    });

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Keys.Enter && !e.Modifiers.HasFlag(Keys.Shift))
        {
            e.Handled = true;
            OnSendOrStop();
            return;
        }

        // Intercept paste only when the clipboard carries an image or file uris; let plain-text
        // paste fall through to the TextArea's own handling.
        bool pasteChord = e.Key == Keys.V && (e.Modifiers.HasFlag(Keys.Application) || e.Modifiers.HasFlag(Keys.Control));
        if (pasteChord && TryPasteAttachment())
            e.Handled = true;
    }

    private void OnSendOrStop()
    {
        if (SendButton.Text == "Stop")
        {
            CancelActive();
            return;
        }
        Send();
    }

    private void CancelActive()
    {
        if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgent agent))
            agent.Cancel();
    }

    private void Send()
    {
        string text = PromptBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 && Pending.Count == 0)
            return;
        if (!TryDoc(out RhinoDoc doc))
            return;

        // Resolve/subscribe before dispatch: the first prompt builds the agent, and we want the
        // Changed hook attached before its reader loop starts writing. Gate on availability so a
        // no-op dispatch doesn't silently discard the user's typed prompt + attachments.
        if (!AgentHost.TryFor(doc, out IAgent _))
        {
            RhinoApp.WriteLine("[rhmcp] No AI agent available; open AI Settings to configure one.");
            return;
        }
        Resubscribe();

        UserMessage message = new(text, Pending.ToArray());
        AgentDispatch.PromptActive(doc, message);

        PromptBox.Text = string.Empty;
        Pending.Clear();
        RefreshAttachmentStrip();
        Rerender();
    }

    private void OnNewConversation()
    {
        if (TryActiveConversation(out Conversation convo))
            PersistConversationHook(convo);

        // Drop the pooled agent so the next prompt rebuilds it with a fresh session id +
        // Conversation. Disposal also kills any in-flight process.
        if (TryDoc(out RhinoDoc doc))
            AgentHost.Stop(doc);

        Pending.Clear();
        RefreshAttachmentStrip();
        Resubscribe();
        PopulateRecents();
        Rerender();
    }

    private void OnPickFile()
    {
        OpenFileDialog dialog = new() { MultiSelect = true, Title = "Attach files" };
        dialog.Filters.Add(new FileFilter("Images", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"));
        dialog.Filters.Add(new FileFilter("Text", ".txt", ".md", ".cs", ".py", ".json", ".csv", ".log"));
        dialog.Filters.Add(new FileFilter("All files", ".*"));

        if (dialog.ShowDialog(this) != DialogResult.Ok)
            return;

        foreach (string path in dialog.Filenames)
            AddFileAttachment(path);
        RefreshAttachmentStrip();
    }

    private void AddFileAttachment(string path)
    {
        if (!File.Exists(path))
            return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string name = Path.GetFileName(path);
        byte[] data = File.ReadAllBytes(path);

        if (IsImageExtension(ext))
            Pending.Add(new Attachment(AttachmentKind.Image, name, MediaTypeForImage(ext), data));
        else
            Pending.Add(new Attachment(AttachmentKind.TextFile, name, "text/plain", data));
    }

    private static bool IsImageExtension(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => true,
        _ => false,
    };

    private static string MediaTypeForImage(string ext) => ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    private bool TryPasteAttachment()
    {
        Clipboard clipboard = Clipboard.Instance;
        if (clipboard.ContainsImage && clipboard.Image is Bitmap bitmap)
        {
            using MemoryStream ms = new();
            bitmap.Save(ms, ImageFormat.Png);
            Pending.Add(new Attachment(AttachmentKind.Image, "pasted.png", "image/png", ms.ToArray()));
            RefreshAttachmentStrip();
            return true;
        }
        if (clipboard.ContainsUris && clipboard.Uris is { Length: > 0 } uris)
        {
            foreach (Uri uri in uris.Where(static u => u.IsFile))
                AddFileAttachment(uri.LocalPath);
            RefreshAttachmentStrip();
            return true;
        }
        return false;
    }

    private void RefreshAttachmentStrip()
    {
        AttachmentStrip.Items.Clear();
        foreach (Attachment att in Pending.ToArray())
            AttachmentStrip.Items.Add(AttachmentChip(att));
    }

    private Control AttachmentChip(Attachment att)
    {
        Label name = new()
        {
            Text = att.Kind == AttachmentKind.Image ? $"▦ {att.Name}" : $"▤ {att.Name}",
            VerticalAlignment = VerticalAlignment.Center,
            Font = SystemFonts.Default(8),
        };
        Button remove = new() { Text = "×", Width = 22, ToolTip = "Remove attachment" };
        remove.Click += (_, _) =>
        {
            Pending.Remove(att);
            RefreshAttachmentStrip();
        };

        return new Panel
        {
            Padding = new Padding(6, 2),
            BackgroundColor = SystemColors.ControlBackground,
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { name, remove },
            },
        };
    }

    private void OpenSettings()
    {
        AISettingsDialog dialog = new();
        dialog.ShowModal(this);
        AgentRegistry.Refresh();
        Reload();
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
