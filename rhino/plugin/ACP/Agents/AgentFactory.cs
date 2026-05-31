using System;

namespace RhMcp;

internal static class AgentFactory
{
    public static IAgent Create(AgentDefinition def) => def.Adapter switch
    {
        AgentAdapter.Claude => new ClaudeCliAgent(def),
        AgentAdapter.Codex => new CodexCliAgent(def),
        _ => throw new ArgumentOutOfRangeException(nameof(def), def.Adapter, "Unknown agent adapter."),
    };
}
