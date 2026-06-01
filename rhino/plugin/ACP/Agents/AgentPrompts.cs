namespace RhMcp;

// Shared system-prompt steering for agents whose CLI lets us inject one (Claude, Codex). The
// built-in AskUserQuestion needs an interactive frontend we don't have in headless stdio mode, so
// steer every agent to the Rhino MCP tool that renders on the command line and in the panel instead.
internal static class AgentPrompts
{
    public static string AskUserSteer =>
        "To ask the user a question or have them choose between options, always call the "
        + $"mcp__{RouterMcpConfig.ServerName}__ask_user tool. Never use the built-in AskUserQuestion "
        + "tool — it cannot be displayed in this environment and will be cancelled.";

    // The always-on steer plus this agent's own prompt; the steer is never dropped.
    public static string Compose(string systemPrompt) =>
        systemPrompt.Length > 0 ? AskUserSteer + "\n\n" + systemPrompt : AskUserSteer;
}
