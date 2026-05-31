using System.Text;

namespace RhMcp;

// Stream chunk kinds; SessionStarted lives at conversation level, not inside a turn.
internal enum TurnEventKind
{
    AssistantText,
    ToolUse,
    Result,
    SessionStarted,
}

// Args/Result are empty for kinds that don't carry them; ToolUse holds the tool's input JSON in
// Args and (once the matching tool_result arrives) its output in Result so a chip can expand both.
internal sealed record TurnEvent(
    TurnEventKind Kind,
    string Text,
    DateTimeOffset At,
    string Args = "",
    string Result = "");

// Mutated only while it is the current turn; Complete() freezes it permanently.
internal sealed class Turn
{
    private object Sync { get; }
    private List<TurnEvent> EventList { get; } = new();

    internal Turn(string prompt, object sync)
    {
        Prompt = prompt;
        StartedAt = DateTimeOffset.UtcNow;
        Sync = sync;
    }

    public string Prompt { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool Completed { get { lock (Sync) return CompletedAt.HasValue; } }

    public IReadOnlyList<TurnEvent> Events { get { lock (Sync) return EventList.ToArray(); } }

    internal void Add(TurnEvent ev) { lock (Sync) EventList.Add(ev); }
    internal void Complete() { lock (Sync) CompletedAt ??= DateTimeOffset.UtcNow; }
}

// One lock guards the whole graph (shared with each Turn): reader thread writes, PromptAsync
// prompts, UI reads. Lifecycle events sit outside turns — a "session started" can arrive
// before the first turn exists.
internal sealed class Conversation
{
    private object Sync { get; } = new();
    private List<Turn> TurnList { get; } = new();
    private List<TurnEvent> LifecycleList { get; } = new();
    private Turn? Current { get; set; }

    // Transient UI state, not transcript history: the panel renders this inline while an ask_user
    // tool call is awaiting an answer. Deliberately kept out of Render() and any persistence so a
    // half-asked question is never serialized. Set/cleared by the ask_user tool body.
    private PendingQuestion? CurrentQuestion { get; set; }

    public Conversation(Guid sessionId) => SessionId = sessionId;

    public Guid SessionId { get; }

    // Raised after every mutation so a panel can re-render. Fired OUTSIDE the lock: handlers
    // marshal to the UI thread and read the graph, which would deadlock if we still held Sync.
    public event Action? Changed;

    // Live references, not a snapshot — the current turn may still be appending.
    public IReadOnlyList<Turn> Turns { get { lock (Sync) return TurnList.ToArray(); } }
    public IReadOnlyList<TurnEvent> Lifecycle { get { lock (Sync) return LifecycleList.ToArray(); } }

    public Turn BeginTurn(string prompt)
    {
        Turn turn;
        lock (Sync)
        {
            turn = new(prompt, Sync);
            TurnList.Add(turn);
            Current = turn;
        }
        Changed?.Invoke();
        return turn;
    }

    public void Record(TurnEventKind kind, string text, string args = "", string result = "")
    {
        lock (Sync)
            Current?.Add(new TurnEvent(kind, text, DateTimeOffset.UtcNow, args, result));
        Changed?.Invoke();
    }

    public void NoteSessionStarted()
    {
        lock (Sync)
            LifecycleList.Add(new TurnEvent(TurnEventKind.SessionStarted, "session started", DateTimeOffset.UtcNow));
        Changed?.Invoke();
    }

    public bool TryGetPendingQuestion(out PendingQuestion question)
    {
        lock (Sync)
        {
            question = CurrentQuestion!;
            return CurrentQuestion is not null;
        }
    }

    public void SetPendingQuestion(PendingQuestion question)
    {
        lock (Sync)
            CurrentQuestion = question;
        Changed?.Invoke();
    }

    // ReferenceEquals-guarded so a finished question clearing late can't wipe a newer one that
    // already replaced it (mirrors AskUserRegistry.Clear).
    public void ClearPendingQuestion(PendingQuestion question)
    {
        lock (Sync)
            if (ReferenceEquals(CurrentQuestion, question))
                CurrentQuestion = null;
        Changed?.Invoke();
    }

    public void CompleteTurn()
    {
        lock (Sync)
        {
            Current?.Complete();
            Current = null;   // stray late events after a terminal event are dropped, not mis-filed
        }
        Changed?.Invoke();
    }

    // Flatten to plain text; assistant chunks appended raw to rejoin the stream.
    public string Render()
    {
        StringBuilder sb = new();
        lock (Sync)
        {
            foreach (TurnEvent ev in LifecycleList)
                sb.AppendLine($"— {ev.Text} —");

            foreach (Turn turn in TurnList)
            {
                sb.AppendLine();
                sb.AppendLine($"> {turn.Prompt}");
                foreach (TurnEvent ev in turn.Events)
                {
                    switch (ev.Kind)
                    {
                        case TurnEventKind.ToolUse:
                            sb.AppendLine($"  ⚙ {ev.Text}");
                            break;
                        case TurnEventKind.Result:
                            sb.AppendLine();
                            sb.AppendLine(ev.Text);
                            break;
                        default:
                            sb.Append(ev.Text);
                            break;
                    }
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
}
