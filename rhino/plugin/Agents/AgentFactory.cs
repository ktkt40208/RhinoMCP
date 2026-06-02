using System;

namespace RhMcp;

internal static class AgentFactory
{
    public static IAgentRunner Create(AgentDefinition def, string docTitle) => def.Adapter switch
    {
        AgentAdapter.Claude => new AgentRunner(def, docTitle, (client, cwd) => new StreamJsonAgent(def, client, cwd, new ClaudeStreamJsonParser(def))),
        AgentAdapter.Codex => new AgentRunner(def, docTitle, (client, cwd) => new StreamJsonAgent(def, client, cwd, new CodexStreamJsonParser(def))),
        AgentAdapter.Gemini => new AgentRunner(def, docTitle, (client, cwd) => GeminiConnection.Connect(def, client, cwd)),
        _ => throw new ArgumentOutOfRangeException(nameof(def), def.Adapter, "Unknown agent adapter."),
    };
}
