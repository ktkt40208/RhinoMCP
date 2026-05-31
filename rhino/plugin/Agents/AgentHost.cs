namespace RhMcp;

internal static class AgentHost
{
    static Dictionary<uint, IAgent> Agents { get; } = new();

    static AgentHost()
    {
        RhinoDoc.CloseDocument += OnCloseDocument;
    }

    static void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        if (Agents.Remove(e.DocumentSerialNumber, out IAgent? agent))
            DisposeSafely(agent);
    }

    public static IAgent For(RhinoDoc doc)
    {
        if (!Agents.TryGetValue(doc.RuntimeSerialNumber, out IAgent? agent))
        {
            agent = new ClaudeCliAgent();
            Agents[doc.RuntimeSerialNumber] = agent;
        }
        return agent;
    }

    public static IAgent? Find(RhinoDoc doc) =>
        Agents.TryGetValue(doc.RuntimeSerialNumber, out IAgent? agent) ? agent : null;

    public static void Stop(RhinoDoc doc)
    {
        if (Agents.Remove(doc.RuntimeSerialNumber, out IAgent? agent))
            DisposeSafely(agent);
    }

    public static void Shutdown()
    {
        foreach (IAgent agent in Agents.Values.ToArray())
            DisposeSafely(agent);
        Agents.Clear();
    }

    static void DisposeSafely(IAgent agent)
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
