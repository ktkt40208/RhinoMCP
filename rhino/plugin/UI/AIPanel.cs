using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoCommand = Rhino.Commands.Command;

namespace RhMcp;

// One panel instance per document (PanelType.PerDoc). Renders the active agent's Conversation as
// chat bubbles + tool chips, sends prompts through the shared AgentDispatch funnel, and re-renders
// off the Conversation.Changed event marshaled onto the UI thread (the reader loop is off-thread).
[Guid("fb948c98-5987-45a3-8dcb-2814ed77ee3b")]
public class AIPAnel : Panel
{
    public static Guid PanelId => typeof(AIPAnel).GUID;

    private uint DocSerial { get; }

    private DropDown AgentPicker { get; } = new();
    private Label ModelLabel { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
    private DropDown RecentPicker { get; } = new() { ToolTip = "Previous conversations" };
    private TextArea PromptBox { get; } = new() { AcceptsReturn = true, AcceptsTab = false, Height = 64 };
    private Button SendButton { get; } = new() { Text = "Send" };
    private StackLayout TranscriptStack { get; } = new()
    {
        Spacing = 8,
        Padding = new Padding(SideMargin, 4),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,   // rows fill width so bubbles can bias left/right
    };
    private StackLayout AttachmentStrip { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

    private Scrollable TranscriptScroll { get; } = new() { ExpandContentWidth = true };

    // Each MessageBubble sizes its own wrapped width/height, but must be re-pinned to the viewport
    // on resize; tool-chip detail labels likewise wrap to the viewport so long JSON never forces a
    // horizontal scroll. LastBudget short-circuits the resize handler when the width budget is
    // unchanged (Rerender resets it to force a re-apply over the freshly built controls).
    private List<MessageBubble> Bubbles { get; } = new();
    private List<Label> ToolDetails { get; } = new();
    private Font MeasureFont { get; } = SystemFonts.Default();
    private int LastBudget { get; set; } = -1;

    private const int SideMargin = 10;        // left/right breathing room around every row
    private const int ScrollbarGuard = 18;    // reserve the vertical scrollbar so content never x-scrolls
    private const int MaxBubbleHeight = 320;  // oversized messages cap here and scroll internally

    private static Image? CopyIconBacking { get; set; }
    private static bool CopyIconLoaded { get; set; }

    private List<Attachment> Pending { get; } = new();

    // Persist a conversation before "New conversation" drops it (turns are already saved on
    // completion; this captures the final state). Overridable so the panel works standalone.
    internal Action<Conversation> PersistConversationHook { get; set; } = ConversationStore.Save;

    // Non-empty while reviewing a persisted transcript: the picker swaps the live view for a
    // read-only render until Back returns to live.
    private ConversationDto? Reviewing { get; set; }

    // Resubscribed on every agent switch; tracked so we can unhook the old one.
    private Conversation? Subscribed { get; set; }
    private Action? SubscribedHandler { get; set; }

    // Suppresses OnAgentPicked while the dropdown is repopulated programmatically, so only a
    // genuine user pick drives SetActive/Resubscribe/Rerender.
    private bool Populating { get; set; }

    // The last agent the user successfully selected; picking a disabled / not-found entry snaps the
    // dropdown back to this since Eto can't disable individual dropdown items.
    private string LastAgentKey { get; set; } = string.Empty;

    public AIPAnel()
        : this(Rhino.RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u)
    {
    }

    public AIPAnel(uint documentSerialNumber)
    {
        DocSerial = documentSerialNumber;

        Padding = new Padding(8);
        TranscriptScroll.Content = TranscriptStack;
        TranscriptScroll.SizeChanged += (_, _) => ApplyBubbleWidths();

        AgentPicker.SelectedValueChanged += OnAgentPicked;
        RecentPicker.SelectedValueChanged += OnRecentPicked;
        SendButton.Click += (_, _) => OnSendOrStop();

        Button settingsGear = new() { Text = "⚙", ToolTip = "AI Settings" };
        settingsGear.Click += (_, _) => OpenSettings();

        Button newConvo = new() { Text = "New", ToolTip = "New conversation" };
        newConvo.Click += (_, _) => OnNewConversation();

        Button attachButton = new() { Text = "+", ToolTip = "Attach a file" };
        attachButton.Click += (_, _) => OnPickFile();

        PromptBox.KeyDown += OnPromptKeyDown;

        // Two rows so the agent picker gets the full panel width instead of fighting the model
        // label / recents / New for space on a narrow docked panel.
        StackLayout headerTop = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new StackLayoutItem(settingsGear, false),
                new StackLayoutItem(AgentPicker, true),
                new StackLayoutItem(newConvo, false),
            },
        };

        StackLayout headerSub = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new StackLayoutItem(ModelLabel, false),
                new StackLayoutItem(RecentPicker, true),
            },
        };

        // Center (not stretch) so the small attach/send buttons keep their natural height instead of
        // growing as tall as the multi-line prompt box (which read as oversized square boxes).
        StackLayout promptRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Center,
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
                new TableRow(headerTop),
                new TableRow(headerSub),
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

    // Resolve only this panel's own document. No ActiveDoc fallback: a PerDoc panel whose document
    // has vanished must no-op, never act on whatever document happens to be active now.
    private bool TryDoc(out RhinoDoc doc)
    {
        if (RhinoDoc.FromRuntimeSerialNumber(DocSerial) is { } own)
        {
            doc = own;
            return true;
        }
        doc = default!;
        return false;
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
                string suffix = !r.Definition.Enabled ? " (disabled)" : !r.Available ? " (not found)" : string.Empty;
                AgentPicker.Items.Add(new ListItem { Text = r.Definition.Name + suffix, Key = r.Definition.Name });
            }

            // Reflect the resolved active agent without pinning: only a genuine user pick should
            // pin via AgentHost.SetActive, so the registry's first-Enabled-and-Available fallback
            // stays authoritative as availability changes.
            if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgent active))
                AgentPicker.SelectedKey = active.Name;
            else if (chain.FirstOrDefault(static r => r.Definition.Enabled && r.Available) is { } available)
                AgentPicker.SelectedKey = available.Definition.Name;
            else if (AgentPicker.Items.Count > 0)
                AgentPicker.SelectedIndex = 0;
        }
        finally
        {
            Populating = false;
        }

        LastAgentKey = AgentPicker.SelectedKey ?? string.Empty;
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
        Populating = true;
        try
        {
            RecentPicker.Items.Clear();
            RecentPicker.Items.Add(new ListItem { Text = "Prev Convos", Key = string.Empty });
            foreach (ConversationDto convo in ConversationStore.List())
                RecentPicker.Items.Add(new ListItem { Text = RecentLabel(convo), Key = convo.SessionId });
            RecentPicker.SelectedIndex = 0;
        }
        finally
        {
            Populating = false;
        }
    }

    private static string RecentLabel(ConversationDto convo)
    {
        string when = convo.StartedAt.ToLocalTime().ToString("MMM d HH:mm");
        string first = convo.Turns.Count > 0 ? convo.Turns[0].Prompt : "(empty)";
        if (first.Length > 32)
            first = first[..32] + "…";
        return $"{when} · {convo.AgentName} · {first}";
    }

    private void OnAgentPicked(object? sender, EventArgs e)
    {
        if (Populating)
            return;

        string? name = AgentPicker.SelectedKey;
        if (string.IsNullOrEmpty(name) || !TryDoc(out RhinoDoc doc))
            return;

        // Disabled / not-installed agents are listed for context but can't be driven, and Eto has no
        // per-item disable — so snap the selection back to the last usable pick instead.
        if (AgentRegistry.Chain.FirstOrDefault(r => r.Definition.Name == name) is not { Definition.Enabled: true, Available: true })
        {
            Populating = true;
            try { AgentPicker.SelectedKey = LastAgentKey; }
            finally { Populating = false; }
            return;
        }

        LastAgentKey = name;
        Reviewing = null;   // switching agents returns to a live view
        AgentHost.SetActive(doc, name);
        UpdateModelLabel();
        Resubscribe();
        Rerender();
    }

    private void OnRecentPicked(object? sender, EventArgs e)
    {
        if (Populating)
            return;

        string? sessionId = RecentPicker.SelectedKey;
        if (string.IsNullOrEmpty(sessionId) || !ConversationStore.TryLoad(sessionId, out ConversationDto convo))
        {
            ExitReview();
            return;
        }
        Reviewing = convo;
        RenderReview(convo);
    }

    // Leave the read-only transcript and return to the live conversation.
    private void ExitReview()
    {
        Reviewing = null;
        Populating = true;
        try { RecentPicker.SelectedIndex = 0; }
        finally { Populating = false; }
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
        if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgent agent))
        {
            convo = agent.Conversation;
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

        // While reviewing a persisted transcript, a live Changed event must not clobber the
        // read-only view; the review is redrawn only by OnRecentPicked / Back.
        if (Reviewing is { } reviewed)
        {
            RenderReview(reviewed);
            return;
        }

        ResetTranscript();

        bool anyAgent = AgentRegistry.Chain.Any(static r => r.Available);
        if (!anyAgent)
        {
            ShowNoAgentState();
            SyncSendButton(false);
            return;
        }

        if (!TryActiveConversation(out Conversation convo))
        {
            RenderItem(new TranscriptItem(TranscriptRole.Agent, "Start a conversation with the active agent."));
            ApplyBubbleWidths();
            SyncSendButton(false);
            return;
        }

        TranscriptViewModel vm = TranscriptViewModel.FromLive(convo);
        foreach (TranscriptItem item in vm.Items)
            RenderItem(item);

        if (convo.TryGetPendingQuestion(out PendingQuestion question))
            TranscriptStack.Items.Add(QuestionCard(question));

        SyncSendButton(vm.Running);
        ApplyBubbleWidths();
        ScrollToBottom();
    }

    // Clear the rendered rows and per-render tracking, and force the next ApplyBubbleWidths to
    // re-pin widths over the freshly built controls even when the viewport width is unchanged.
    private void ResetTranscript()
    {
        TranscriptStack.Items.Clear();
        Bubbles.Clear();
        ToolDetails.Clear();
        LastBudget = -1;
    }

    private void RenderItem(TranscriptItem item)
    {
        Control control = item.Role switch
        {
            TranscriptRole.System => SystemLine(item.Text),
            TranscriptRole.Tool => ToolChip(item),
            _ => BubbleRow(item),
        };
        TranscriptStack.Items.Add(control);
    }

    // A bubble biased left (agent) or right (user) by a stretchable spacer on the open side; the
    // transcript's side padding plus the spacer give every row left/right breathing room.
    private Control BubbleRow(TranscriptItem item)
    {
        bool user = item.Role == TranscriptRole.User;
        MessageBubble bubble = new(item.Text, user, MeasureFont, MaxBubbleHeight, CopyIcon());
        Bubbles.Add(bubble);

        StackLayout row = new() { Orientation = Orientation.Horizontal };
        StackLayoutItem flex = new(null, true);
        StackLayoutItem fixedBubble = new(bubble, false);
        if (user)
        {
            row.Items.Add(flex);
            row.Items.Add(fixedBubble);
        }
        else
        {
            row.Items.Add(fixedBubble);
            row.Items.Add(flex);
        }
        return row;
    }

    // Inline ask_user affordance rendered at the bottom of the transcript. Click handlers run on
    // the UI thread and only touch managed state + the thread-safe PendingQuestion TCS — no Rhino
    // modal Get APIs — so the question is safe to answer mid-command. First channel to complete
    // the TCS wins; the other (command line) call returns false and is ignored.
    private Control QuestionCard(PendingQuestion question)
    {
        StackLayout body = new() { Spacing = 6, Padding = new Padding(8, 6) };
        body.Items.Add(new Label { Text = $"ask_user: {question.Question}", Font = SystemFonts.Bold() });

        if (question.Task.IsCompleted)
        {
            body.Items.Add(new Label
            {
                Text = "answered",
                TextColor = SystemColors.DisabledText,
                Font = SystemFonts.Default(8),
            });
            return Card(body);
        }

        TextBox otherText = new() { PlaceholderText = "Other…" };

        if (question.Mode == AskUserMode.Multi)
        {
            List<CheckBox> checks = [];
            foreach (string option in question.Options)
            {
                CheckBox box = new() { Text = option };
                checks.Add(box);
                body.Items.Add(box);
            }
            body.Items.Add(otherText);

            Button submit = new() { Text = "Submit" };
            submit.Click += (_, _) =>
            {
                List<string> selected = [];
                for (int i = 0; i < checks.Count; i++)
                    if (checks[i].Checked == true)
                        selected.Add(question.Options[i]);
                string other = otherText.Text?.Trim() ?? string.Empty;
                if (other.Length > 0)
                    selected.Add(other);
                CompleteFromPanel(question, selected);
            };
            body.Items.Add(ButtonRow(question, submit));
            return Card(body);
        }

        RadioButton controller = new() { Text = question.Options.Count > 0 ? question.Options[0] : "Other" };
        List<RadioButton> radios = [];
        for (int i = 0; i < question.Options.Count; i++)
        {
            RadioButton radio = i == 0 ? controller : new RadioButton(controller) { Text = question.Options[i] };
            radios.Add(radio);
            body.Items.Add(radio);
        }
        RadioButton otherRadio = question.Options.Count == 0 ? controller : new RadioButton(controller) { Text = "Other" };
        if (question.Options.Count != 0)
            body.Items.Add(otherRadio);
        body.Items.Add(otherText);

        Button submitSingle = new() { Text = "Submit" };
        submitSingle.Click += (_, _) =>
        {
            for (int i = 0; i < radios.Count; i++)
            {
                if (radios[i].Checked)
                {
                    CompleteFromPanel(question, [question.Options[i]]);
                    return;
                }
            }
            string other = otherText.Text?.Trim() ?? string.Empty;
            CompleteFromPanel(question, other.Length > 0 ? [other] : []);
        };
        body.Items.Add(ButtonRow(question, submitSingle));
        return Card(body);
    }

    private Control ButtonRow(PendingQuestion question, Button submit)
    {
        Button cancel = new() { Text = "Cancel" };
        cancel.Click += (_, _) =>
        {
            question.TryCancel();
            Rerender();
        };
        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Items = { submit, cancel },
        };
    }

    private void CompleteFromPanel(PendingQuestion question, IReadOnlyList<string> selected)
    {
        question.TryComplete(AskUserAnswer.Of(selected));
        Rerender();   // defensive immediate dismissal; the tool's finally also clears the card
    }

    private static Control Card(Control content) => new Panel
    {
        Padding = new Padding(2),
        BackgroundColor = SystemColors.ControlBackground,
        Content = content,
    };

    // Read-only render of a persisted transcript. Mirrors the live layout (bubbles + tool chips)
    // but drives off the DTO and is never mutated by the off-thread Changed event.
    private void RenderReview(ConversationDto convo)
    {
        if (!Loaded)
            return;

        ResetTranscript();

        Button back = new() { Text = "← Back to live" };
        back.Click += (_, _) => ExitReview();
        TranscriptStack.Items.Add(back);
        TranscriptStack.Items.Add(SystemLine($"{convo.DocTitle} · {convo.AgentName} (read-only)"));

        TranscriptViewModel vm = TranscriptViewModel.FromReview(convo);
        foreach (TranscriptItem item in vm.Items)
            RenderItem(item);

        SyncSendButton(false);
        SendButton.Enabled = false;
        ApplyBubbleWidths();
        ScrollToBottom();
    }

    private static Control SystemLine(string text) => new Label
    {
        Text = $"— {text} —",
        TextAlignment = TextAlignment.Center,
        TextColor = SystemColors.DisabledText,
        Font = SystemFonts.Default(7),
    };

    // Compact one-line chip; click toggles an expander showing the tool's args + result. The detail
    // label is tracked so ApplyBubbleWidths can wrap it to the viewport (long JSON otherwise widens
    // the transcript and forces a horizontal scroll).
    private Control ToolChip(TranscriptItem item)
    {
        string detail = BuildToolDetail(item.ToolArgs, item.ToolResult);
        Label headerLabel = new()
        {
            Text = $"⚙ {item.Text}",
            Font = SystemFonts.Default(8),
            TextColor = SystemColors.ControlText,
        };

        if (detail.Length == 0)
            return new Panel { Padding = new Padding(6, 3), Content = headerLabel };

        Label detailLabel = new()
        {
            Text = detail,
            Wrap = WrapMode.Word,
            Font = SystemFonts.Default(8),
            TextColor = SystemColors.DisabledText,
        };
        ToolDetails.Add(detailLabel);

        Expander expander = new()
        {
            Header = headerLabel,
            Expanded = false,
            Content = detailLabel,
        };
        return new Panel { Padding = new Padding(6, 3), Content = expander };
    }

    private static string BuildToolDetail(string args, string result)
    {
        System.Text.StringBuilder sb = new();
        if (!string.IsNullOrWhiteSpace(args))
            sb.Append("args: ").Append(args);
        if (!string.IsNullOrWhiteSpace(result))
        {
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append("result: ").Append(result);
        }
        return sb.ToString();
    }

    // The embedded copy.svg rendered once to an Eto bitmap (via Rhino's SVG rasterizer, dark-mode
    // aware) and shared across every bubble's copy button. Null falls back to a text "Copy" button.
    private static Image? CopyIcon()
    {
        if (CopyIconLoaded)
            return CopyIconBacking;
        CopyIconLoaded = true;

        try
        {
            Assembly assembly = typeof(AIPAnel).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream("RhMcp.copy.svg");
            if (stream is null)
                return CopyIconBacking;

            using StreamReader reader = new(stream);
            string svg = reader.ReadToEnd();
            using System.Drawing.Bitmap rendered = Rhino.UI.DrawingUtilities.BitmapFromSvg(svg, 14, 14, adjustForDarkMode: true);
            using MemoryStream png = new();
            rendered.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            CopyIconBacking = new Bitmap(png.ToArray());
        }
        catch
        {
            CopyIconBacking = null;
        }
        return CopyIconBacking;
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
        SendButton.Enabled = true;   // re-enable after a read-only review disabled it
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

    // The width budget every row wraps to: the viewport minus the transcript's side padding and a
    // reserve for the vertical scrollbar, so nothing reports a width wider than the viewport (which
    // would otherwise show a horizontal scrollbar). Re-runs on resize; a no-op when width is stable.
    private void ApplyBubbleWidths()
    {
        int viewport = TranscriptScroll.ClientSize.Width;
        if (viewport <= 0)
            return;
        int budget = Math.Max(80, viewport - 2 * SideMargin - ScrollbarGuard);
        if (budget == LastBudget)
            return;
        LastBudget = budget;

        foreach (MessageBubble bubble in Bubbles)
            bubble.Apply(budget);
        foreach (Label detail in ToolDetails)
            detail.Width = budget;
    }

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
        if (Reviewing is not null)   // read-only: Enter/click must not drive the live conversation
            return;
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
        Reviewing = null;   // a fresh conversation is always a live view
        if (TryActiveConversation(out Conversation convo))
            PersistConversationHook(convo);

        // Drop the pooled agent so the next prompt rebuilds it with a fresh session id +
        // Conversation. Disposal also kills any in-flight process. Stop() also forgets the pinned
        // active agent, so re-pin the dropdown's selection — otherwise New silently snaps back to
        // the configured default while the picker still shows the old choice.
        if (TryDoc(out RhinoDoc doc))
        {
            AgentHost.Stop(doc);
            if (LastAgentKey.Length > 0)
                AgentHost.SetActive(doc, LastAgentKey);
        }

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
