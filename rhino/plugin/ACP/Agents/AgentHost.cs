using System.Collections.Generic;
using System.Linq;

namespace RhMcp;

internal static class AgentHost
{
    // Keyed by (document serial, agent name) so each doc can drive one agent per kind
    // (Claude, Codex, ...) at once without them colliding on a single slot.
    private static Dictionary<(uint Doc, string Name), IAgent> Agents { get; } = new();

    // The active agent name per document. Absent => fall back to the configured default.
    private static Dictionary<uint, string> ActiveNames { get; } = new();

    static AgentHost()
    {
        RhinoDoc.CloseDocument += OnCloseDocument;
    }

    private static void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        DisposeDoc(e.DocumentSerialNumber);
    }

    public static void SetActive(RhinoDoc doc, string name) =>
        ActiveNames[doc.RuntimeSerialNumber] = name;

    // The active agent for the doc, resolved via the registry and pooled per (doc, name).
    // Returns false (rather than null) when discovery finds nothing usable, so callers can
    // surface a friendly message instead of faulting.
    public static bool TryFor(RhinoDoc doc, out IAgent agent)
    {
        if (!TryResolveActiveDefinition(doc, out AgentDefinition def))
        {
            agent = default!;
            return false;
        }
        string docTitle = DocTitle(doc);
        agent = For(doc, () => AgentFactory.Create(def, docTitle));
        return true;
    }

    // Saved file name, else a stable placeholder so a transcript is still identifiable.
    private static string DocTitle(RhinoDoc doc) =>
        string.IsNullOrEmpty(doc.Name) ? "Untitled" : doc.Name;

    private static bool TryResolveActiveDefinition(RhinoDoc doc, out AgentDefinition def)
    {
        if (ActiveNames.TryGetValue(doc.RuntimeSerialNumber, out string? active) &&
            AgentRegistry.TryGet(active, out def))
            return true;

        return AgentRegistry.TryResolveActive(out def);
    }

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
        ActiveNames.Clear();
    }

    private static void DisposeDoc(uint serial)
    {
        foreach ((uint Doc, string Name) key in Agents.Keys.Where(k => k.Doc == serial).ToArray())
        {
            if (Agents.Remove(key, out IAgent? agent))
                SafeDispose(agent);
        }
        ActiveNames.Remove(serial);
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
