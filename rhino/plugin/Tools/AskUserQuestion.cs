using System.Threading.Tasks;

namespace RhMcp;

internal enum AskUserMode { Single, Multi }

// Wire-shape carrier; both channels resolve a question into one of these.
internal sealed record AskUserAnswer(IReadOnlyList<string> Selected, bool Cancelled)
{
    public static AskUserAnswer Cancel { get; } = new([], true);

    public static AskUserAnswer Of(IReadOnlyList<string> selected) => new(selected, false);
}

// The single coordination point both the panel and the command-line channel race on. Whoever
// calls TryComplete/TryCancel first wins; TaskCompletionSource.TrySetResult is atomic so the
// loser's call returns false and the awaiting tool body resumes exactly once.
internal sealed class PendingQuestion
{
    public PendingQuestion(string question, IReadOnlyList<string> options, AskUserMode mode)
    {
        Question = question;

        // Collapse any agent-supplied literal "Other" so the synthesized free-text affordance
        // never duplicates a real option in either channel.
        List<string> kept = [];
        foreach (string option in options)
            if (!IsOther(option))
                kept.Add(option);
        Options = kept;
        Mode = mode;
    }

    public string Question { get; }
    public IReadOnlyList<string> Options { get; }
    public AskUserMode Mode { get; }

    private TaskCompletionSource<AskUserAnswer> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<AskUserAnswer> Task => Completion.Task;

    public bool TryComplete(AskUserAnswer answer) => Completion.TrySetResult(answer);

    public bool TryCancel() => Completion.TrySetResult(AskUserAnswer.Cancel);

    public static bool IsOther(string label) =>
        string.Equals(label?.Trim(), "Other", StringComparison.OrdinalIgnoreCase);
}
