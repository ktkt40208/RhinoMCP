using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from RhMcp.Server.ContentBlock

namespace RhMcp;

// The Claude Code stream-json strategy: turns one `claude` stdout line into ACP session/update
// events and frames one user turn for stdin. Owns no process/threads/turn gating (the runner does);
// stays RhinoApp- and AISettings-free so it Compile Include's into a test project. Model, extra args
// and the system prompt come from the ctor-injected Definition; the MCP server set is resolved by
// the runner and handed in per spawn.
internal sealed class ClaudeStreamJsonParser : IStreamJsonParser
{
    private AgentDefinition Definition { get; }

    public ClaudeStreamJsonParser(AgentDefinition definition)
    {
        Definition = definition;
    }

    public string DisplayName => Definition.Name;

    public string NotFoundMessage => "Claude CLI not found. Install Claude Code (claude.ai/install).";

    public void ConfigureArguments(ProcessStartInfo psi, string mcpUrl, string agentSessionId, IReadOnlyList<string> mcpServers, bool resume)
    {
        // Same {"mcpServers":{...}} shape Claude Code expects; rhino points at this doc's HTTP
        // listener (not the router) so the agent always operates on the exact doc. Extra servers the
        // runner resolved are merged in, but never the reserved 'rhino' key.
        JsonObject servers = new()
        {
            ["rhino"] = new JsonObject { ["type"] = "http", ["url"] = mcpUrl },
        };
        foreach (string entry in mcpServers)
            if (JsonNode.Parse(entry) is JsonObject obj)
                foreach (KeyValuePair<string, JsonNode?> kvp in obj)
                    if (kvp.Key != "rhino" && kvp.Value is JsonNode node)
                        servers[kvp.Key] = node.DeepClone();

        string mcpConfig = new JsonObject { ["mcpServers"] = servers }.ToJsonString(McpSerializer.Options);

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--input-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--mcp-config");
        psi.ArgumentList.Add(mcpConfig);
        psi.ArgumentList.Add("--strict-mcp-config");
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("mcp__rhino*");
        psi.ArgumentList.Add("--append-system-prompt");
        psi.ArgumentList.Add(AgentPrompts.Compose(Definition.SystemPrompt));
        psi.ArgumentList.Add("--disable-slash-commands");
        psi.ArgumentList.Add(resume ? "--resume" : "--session-id");
        psi.ArgumentList.Add(agentSessionId);

        if (Definition.Model.Length > 0)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(Definition.Model);
        }
        foreach (string arg in Definition.ExtraArgs)
            psi.ArgumentList.Add(arg);
    }

    // ACP content blocks -> Claude's stream-json user content (text + base64 image). Underscored
    // property names survive the camelCase policy, so media_type stays media_type.
    public string FormatTurn(IReadOnlyList<ContentBlock> prompt)
    {
        List<object> content = [];
        foreach (ContentBlock block in prompt)
        {
            switch (block)
            {
                case TextContentBlock text:
                    content.Add(new { type = "text", text = text.Text });
                    break;
                case ImageContentBlock image:
                    content.Add(new
                    {
                        type = "image",
                        source = new { type = "base64", media_type = image.MimeType, data = image.Data },
                    });
                    break;
            }
        }

        return JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content },
        }, McpSerializer.Options);
    }

    public ParsedLine Parse(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("type", out JsonElement typeEl))
            return ParsedLine.None;

        // Unknown event types fall through to ParsedLine.None on purpose: stream-json carries event
        // kinds we do not translate (system, etc.), and a new one must never fault the turn. This is
        // a deliberate fail-soft, not an unhandled-case discard.
        return typeEl.GetString() switch
        {
            "assistant" => EmitAssistant(root),
            "user" => EmitToolResults(root),
            "result" => ParsedLine.Complete(StopReason.EndTurn),
            _ => ParsedLine.None,
        };
    }

    private static ParsedLine EmitAssistant(JsonElement root)
    {
        if (!TryGetContent(root, out JsonElement content))
            return ParsedLine.None;

        List<SessionUpdate> updates = [];
        foreach (JsonElement block in content.EnumerateArray())
        {
            switch (block.TryGetProperty("type", out JsonElement bt) ? bt.GetString() : null)
            {
                case "text" when block.TryGetProperty("text", out JsonElement text):
                    updates.Add(new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = text.GetString() ?? string.Empty } });
                    break;
                case "tool_use":
                    updates.Add(new ToolCallSessionUpdate
                    {
                        ToolCallId = block.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? string.Empty : string.Empty,
                        Title = block.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty,
                        // Clone so the element outlives the `using JsonDocument` above.
                        RawInput = block.TryGetProperty("input", out JsonElement input) ? input.Clone() : null,
                    });
                    break;
            }
        }
        return updates.Count > 0 ? new ParsedLine(updates, false, StopReason.EndTurn) : ParsedLine.None;
    }

    private static ParsedLine EmitToolResults(JsonElement root)
    {
        if (!TryGetContent(root, out JsonElement content))
            return ParsedLine.None;

        List<SessionUpdate> updates = [];
        foreach (JsonElement block in content.EnumerateArray())
        {
            if ((block.TryGetProperty("type", out JsonElement bt) ? bt.GetString() : null) != "tool_result")
                continue;
            updates.Add(new ToolCallUpdateSessionUpdate
            {
                ToolCallId = block.TryGetProperty("tool_use_id", out JsonElement id) ? id.GetString() ?? string.Empty : string.Empty,
                Status = ToolCallStatus.Completed,
                // Clone so the element outlives the `using JsonDocument` above.
                RawOutput = block.TryGetProperty("content", out JsonElement output) ? output.Clone() : null,
            });
        }
        return updates.Count > 0 ? new ParsedLine(updates, false, StopReason.EndTurn) : ParsedLine.None;
    }

    private static bool TryGetContent(JsonElement root, out JsonElement content)
    {
        content = default;
        return root.TryGetProperty("message", out JsonElement message)
            && message.TryGetProperty("content", out content)
            && content.ValueKind == JsonValueKind.Array;
    }
}
