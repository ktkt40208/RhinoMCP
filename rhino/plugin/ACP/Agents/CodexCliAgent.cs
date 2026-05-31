using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace RhMcp;

// CliAgent over the Codex CLI (`codex`). Parses Codex's own JSON event stream
// (the {"msg":{"type":...}} envelope) instead of Claude's stream-json shape.
internal sealed class CodexCliAgent : CliAgent
{
    public CodexCliAgent(AgentDefinition def, string docTitle) : base(def, docTitle) { }

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

        // External servers from settings, best-effort translated to -c overrides beside rhino.
        // rhino is never overwritten — those tools are the agent's hands on this exact doc.
        if (TryGetExtraMcpServers(out JsonObject extra))
        {
            foreach (KeyValuePair<string, JsonNode?> entry in extra)
            {
                if (entry.Key == "rhino" || entry.Value is not JsonObject server)
                    continue;
                foreach (KeyValuePair<string, JsonNode?> field in server)  // verify: Codex -c mcp_servers.<name>.<key> shape + transport key name
                {
                    if (field.Value is not JsonValue value)
                        continue;
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add($"mcp_servers.{entry.Key}.{field.Key}={EncodeCodexValue(value)}");
                }
            }
        }

        // Append the composed system prompt (AskUserSteer + def.SystemPrompt). Always non-empty
        // because AskUserSteer is always present. // verify: Codex system-prompt flag name
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"experimental_instructions={EncodeCodexString(ComposedSystemPrompt)}");

        // Resume the same conversation across respawns rather than starting over.
        psi.ArgumentList.Add(Started ? "--resume" : "--session-id");  // verify: Codex resume flag names
        psi.ArgumentList.Add(SessionId.ToString());

        // Built-in defaults set Model="" / ExtraArgs=[], so these append nothing; custom
        // entries layer their model/args on top. // verify: Codex -c model override key shape
        if (Definition.Model.Length > 0)
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"model=\"{Definition.Model}\"");
        }
        AppendExtraArgs(psi);
    }

    // A Codex -c value: strings get TOML-style double-quoting; numbers/bools pass through bare.
    // verify: Codex -c value quoting rules
    private static string EncodeCodexValue(JsonValue value) =>
        value.TryGetValue(out string? text)
            ? EncodeCodexString(text)
            : value.ToJsonString();

    private static string EncodeCodexString(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    // Codex `exec -` consumes a plain-text prompt from stdin (no JSON envelope). The agent has no
    // filesystem access, so text files are inlined; images degrade to a short note since this path
    // has no inline-image support. // verify: whether Codex exec - treats embedded newlines as one prompt
    protected override string FormatUserMessage(UserMessage message)
    {
        StringBuilder builder = new();
        builder.Append(message.Text);

        foreach (Attachment attachment in message.Attachments)
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(attachment.Kind switch
            {
                AttachmentKind.TextFile => $"```{attachment.Name}\n{Encoding.UTF8.GetString(attachment.Data)}\n```",
                AttachmentKind.Image => $"[image: {attachment.Name} omitted — this agent has no inline-image support]",
                _ => string.Empty,
            });
        }

        return builder.ToString();
    }

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
