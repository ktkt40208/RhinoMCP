using System.Diagnostics;

namespace RhMcp;

// CliAgent over the Codex CLI (`codex`). Parses Codex's own JSON event stream
// (the {"msg":{"type":...}} envelope) instead of Claude's stream-json shape.
internal sealed class CodexCliAgent : CliAgent
{
    public override string Name => "codex";

    protected override string CommandFileName => "codex";

    protected override string NotFoundMessage =>
        "Codex CLI not found. Install Codex (npm i -g @openai/codex).";

    protected override void ConfigureArguments(ProcessStartInfo psi, string mcpUrl)
    {
        psi.ArgumentList.Add("exec");                 // non-interactive run; prompt arrives on stdin
        psi.ArgumentList.Add("--json");               // emit the JSON event stream  // verify: flag may be `--experimental-json`
        psi.ArgumentList.Add("-");                    // read the prompt from stdin
        // Register Rhino as an MCP server via config override; Codex's [mcp_servers] table
        // takes a streamable-http server keyed by name. // verify: -c override key/url shape
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"mcp_servers.rhino.url=\"{mcpUrl}\"");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("mcp_servers.rhino.type=\"http\"");  // verify: Codex http transport key name
        // Resume the same conversation across respawns rather than starting over.
        psi.ArgumentList.Add(Started ? "--resume" : "--session-id");  // verify: Codex resume flag names
        psi.ArgumentList.Add(SessionId.ToString());
    }

    // Codex `exec -` consumes a plain-text prompt from stdin (no JSON envelope).
    protected override string FormatUserMessage(string text) => text;

    // Codex frames each event as a top-level object carrying a `msg` payload whose `type`
    // names the event (e.g. session_configured, agent_message, mcp_tool_call, task_complete).
    protected override void Handle(string line, Process proc)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;

        JsonElement msg = root.TryGetProperty("msg", out JsonElement m) ? m : root;  // verify: events may nest under `msg`
        if (!msg.TryGetProperty("type", out JsonElement typeEl))
            return;

        switch (typeEl.GetString())
        {
            case "session_configured":   // verify: Codex init event name
                EmitSessionStarted();
                break;
            case "agent_message":        // verify: assistant text event name
            case "mcp_tool_call":        // verify: tool-call event name
                EchoAssistant(msg);
                break;
            case "task_complete":        // verify: terminal/result event name
                if (msg.TryGetProperty("last_agent_message", out JsonElement res) && res.ValueKind is JsonValueKind.String)
                    EmitResult(res.GetString());
                CompleteTurn(proc);   // the terminal event ends the current turn
                break;
        }
    }

    void EchoAssistant(JsonElement msg)
    {
        // Assistant text rides directly on the event; tool calls carry the invoked tool name.
        if (msg.TryGetProperty("message", out JsonElement text) && text.ValueKind is JsonValueKind.String)
        {
            EmitAssistantText(text.GetString());
            return;
        }
        if (msg.TryGetProperty("tool", out JsonElement tool) && tool.ValueKind is JsonValueKind.String)  // verify: mcp_tool_call field name
            EmitToolUse(tool.GetString());
    }
}
