using System;

namespace RhMcp;

internal static class AgentFactory
{
    public static IAgentRunner Create(AgentDefinition def, string docTitle) => def.Adapter switch
    {
        AgentAdapter.Claude => new AgentRunner(def, docTitle, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new ClaudeStreamJsonParser(def))),
        AgentAdapter.Codex => new AgentRunner(def, docTitle, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new CodexStreamJsonParser(def))),
        AgentAdapter.Gemini => new AgentRunner(def, docTitle, (client, _, cwd) => GeminiConnection.Connect(def, client, cwd)),
        _ => throw new ArgumentOutOfRangeException(nameof(def), def.Adapter, "Unknown agent adapter."),
    };
}
