namespace RhMcp;

internal static class AgentHost
{
    // Keyed by (document serial, agent name) so each doc can drive one agent per kind
    // (Claude, Cursor, ...) at once without them colliding on a single slot.
    private static Dictionary<(uint Doc, string Name), IAgent> Agents { get; } = new();

    static AgentHost()
    {
        RhinoDoc.CloseDocument += OnCloseDocument;
    }

    private static void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        DisposeDoc(e.DocumentSerialNumber);
    }

    // The command-line interceptor routes to the Claude agent by default.
    public static IAgent For(RhinoDoc doc) => For(doc, static () => new ClaudeCliAgent());

    public static IAgent For(RhinoDoc doc, Func<IAgent> factory)
    {
        IAgent probe = factory();
        (uint, string) key = (doc.RuntimeSerialNumber, probe.Name);
        if (Agents.TryGetValue(key, out IAgent? existing))
        {
            SafeDispose(probe);
            return existing;
        }
        Agents[key] = probe;
        return probe;
    }

    public static IAgent? Find(RhinoDoc doc) =>
        Agents.FirstOrDefault(kv => kv.Key.Doc == doc.RuntimeSerialNumber).Value;

    public static void Stop(RhinoDoc doc)
    {
        DisposeDoc(doc.RuntimeSerialNumber);
    }

    public static void Shutdown()
    {
        foreach (IAgent agent in Agents.Values.ToArray())
            SafeDispose(agent);
        Agents.Clear();
    }

    private static void DisposeDoc(uint serial)
    {
        foreach ((uint Doc, string Name) key in Agents.Keys.Where(k => k.Doc == serial).ToArray())
        {
            if (Agents.Remove(key, out IAgent? agent))
                SafeDispose(agent);
        }
    }

    private static void SafeDispose(IAgent agent)
    {
        try
        {
            agent.Dispose();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[agenthost] dispose failed: {ex.Message}");
        }
    }
}
