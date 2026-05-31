namespace RhMcp;

// Per-doc lookup for the off-thread CommandInterceptor: Register/Clear run on the MCP request
// thread while TryGet runs on the RhinoApp.Idle pump, so every access is lock-guarded. Keyed by
// doc RuntimeSerialNumber, mirroring AgentHost.ActiveNames.
internal static class AskUserRegistry
{
    private static object Gate { get; } = new();
    private static Dictionary<uint, PendingQuestion> Pending { get; } = new();

    // Cancel and replace any prior pending question for the doc so a stale one can't linger and
    // intercept entries meant for the new question.
    public static void Register(uint docSerial, PendingQuestion question)
    {
        lock (Gate)
        {
            if (Pending.TryGetValue(docSerial, out PendingQuestion? prior))
                prior.TryCancel();
            Pending[docSerial] = question;
        }
    }

    public static bool TryGet(uint docSerial, out PendingQuestion question)
    {
        lock (Gate)
            return Pending.TryGetValue(docSerial, out question!);
    }

    // ReferenceEquals-guarded: a late clear from a finished question must not drop a newer one
    // that already replaced it.
    public static void Clear(uint docSerial, PendingQuestion question)
    {
        lock (Gate)
            if (Pending.TryGetValue(docSerial, out PendingQuestion? current) && ReferenceEquals(current, question))
                Pending.Remove(docSerial);
    }
}
