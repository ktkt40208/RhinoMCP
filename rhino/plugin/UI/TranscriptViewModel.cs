using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RhMcp;

internal enum TranscriptRole
{
    System,
    User,
    Agent,
    Tool,
}

// One rendered row in the transcript. Dumb, immutable. Tool rows carry the call's Args/Result for
// the expander; bubble/system rows leave them empty.
internal readonly record struct TranscriptItem(
    TranscriptRole Role,
    string Text,
    string ToolArgs = "",
    string ToolResult = "");

// Flattens a live Conversation or a persisted ConversationDto into the ordered rows the panel
// renders. Assistant text chunks are coalesced into one bubble per run, and a tool call collapses
// into a single chip carrying its args + result — so raw tool JSON never lands in a bubble. The
// same shaping serves both the live and read-only views.
internal sealed class TranscriptViewModel
{
    public IReadOnlyList<TranscriptItem> Items { get; }
    public bool Running { get; }

    private TranscriptViewModel(IReadOnlyList<TranscriptItem> items, bool running)
    {
        Items = items;
        Running = running;
    }

    public static TranscriptViewModel FromLive(Conversation convo)
    {
        List<TranscriptItem> items = [];
        foreach (TurnEvent ev in convo.Lifecycle)
            items.Add(new TranscriptItem(TranscriptRole.System, ev.Text));

        bool running = false;
        foreach (Turn turn in convo.Turns)
        {
            items.Add(new TranscriptItem(TranscriptRole.User, turn.Prompt));
            Flatten(items, turn.Events.Select(static ev => (ev.Kind, ev.Text, ev.Args, ev.Result)));
            running = !turn.Completed;
        }
        return new TranscriptViewModel(items, running);
    }

    public static TranscriptViewModel FromReview(ConversationDto convo)
    {
        List<TranscriptItem> items = [];
        foreach (TurnEventDto ev in convo.Lifecycle)
            items.Add(new TranscriptItem(TranscriptRole.System, ev.Text));

        foreach (TurnDto turn in convo.Turns)
        {
            items.Add(new TranscriptItem(TranscriptRole.User, turn.Prompt));
            Flatten(items, turn.Events.Select(static ev => (ev.Kind, ev.Text, ev.Args, ev.Result)));
        }
        return new TranscriptViewModel(items, running: false);
    }

    private static void Flatten(
        List<TranscriptItem> items,
        IEnumerable<(TurnEventKind Kind, string Text, string Args, string Result)> events)
    {
        StringBuilder assistant = new();
        void FlushAssistant()
        {
            if (assistant.Length == 0)
                return;
            items.Add(new TranscriptItem(TranscriptRole.Agent, assistant.ToString()));
            assistant.Clear();
        }

        foreach ((TurnEventKind Kind, string Text, string Args, string Result) ev in events)
        {
            switch (ev.Kind)
            {
                case TurnEventKind.AssistantText:
                    assistant.Append(ev.Text);
                    break;
                case TurnEventKind.ToolUse:
                    FlushAssistant();
                    items.Add(new TranscriptItem(TranscriptRole.Tool, ev.Text, ev.Args, ev.Result));
                    break;
                case TurnEventKind.Result:
                    FlushAssistant();
                    if (!string.IsNullOrWhiteSpace(ev.Text))
                        items.Add(new TranscriptItem(TranscriptRole.Agent, ev.Text));
                    break;
            }
        }
        FlushAssistant();
    }
}
