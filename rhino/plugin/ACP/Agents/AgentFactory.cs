using System;

namespace RhMcp;

internal static class AgentFactory
{
    public static IAgent Create(AgentDefinition def, string docTitle) => def.Adapter switch
    {
        AgentAdapter.Claude => new AcpAgent(def, docTitle, (client, cwd) => new ClaudeAcpAgent(def, client, cwd)),
        AgentAdapter.Codex => new CodexCliAgent(def, docTitle),
        AgentAdapter.Gemini => new AcpAgent(def, docTitle, (client, cwd) => GeminiConnection.Connect(def, client, cwd)),
        _ => throw new ArgumentOutOfRangeException(nameof(def), def.Adapter, "Unknown agent adapter."),
    };
}
