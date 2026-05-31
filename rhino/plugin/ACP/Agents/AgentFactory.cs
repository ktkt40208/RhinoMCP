using System;

namespace RhMcp;

internal static class AgentFactory
{
    public static IAgent Create(AgentDefinition def, string docTitle) => def.Adapter switch
    {
        AgentAdapter.Claude => new ClaudeCliAgent(def, docTitle),
        AgentAdapter.Codex => new CodexCliAgent(def, docTitle),
        _ => throw new ArgumentOutOfRangeException(nameof(def), def.Adapter, "Unknown agent adapter."),
    };
}
