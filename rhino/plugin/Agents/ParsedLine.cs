using System.Collections.Generic;
using Acp;

namespace RhMcp;

// A dumb immutable result of parsing one CLI stdout line.
// Updates: the ACP session/update payloads to forward (may be empty).
// IsTurnComplete: true exactly on the CLI's terminal event (Claude 'result', Codex 'task_complete').
// Reason: the StopReason to resolve the turn with when complete (EndTurn on normal completion).
// Build only via the None/Emit/Complete factories so the three shapes stay the only valid states.
internal readonly record struct ParsedLine(
    IReadOnlyList<SessionUpdate> Updates,
    bool IsTurnComplete,
    StopReason Reason)
{
    public static ParsedLine None { get; } = new([], false, StopReason.EndTurn);

    public static ParsedLine Emit(params SessionUpdate[] updates) => new(updates, false, StopReason.EndTurn);

    public static ParsedLine Complete(StopReason reason) => new([], true, reason);
}
