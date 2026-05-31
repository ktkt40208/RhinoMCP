using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace RhMcp;

// CliAgent over Claude Code's stream-json stdio mode.
internal sealed class ClaudeCliAgent : CliAgent
{
    public ClaudeCliAgent(AgentDefinition def) : base(def) { }

    protected override string NotFoundMessage =>
        "Claude CLI not found. Install Claude Code (claude.ai/install).";

    protected override void ConfigureArguments(ProcessStartInfo psi, string mcpUrl)
    {
        // Same {"mcpServers":{...}} shape Claude Code expects; this is the agent's hands.
        // We connect straight to this doc's HTTP listener — not via the router — so the
        // agent always operates on the exact doc the command was run in.
        JsonObject servers = new()
        {
            ["rhino"] = new JsonObject { ["type"] = "http", ["url"] = mcpUrl },
        };

        // External servers from settings are merged beside rhino, but rhino is never
        // overwritten — those tools are the agent's hands on this exact doc.
        if (TryGetExtraMcpServers(out JsonObject extra))
        {
            foreach (KeyValuePair<string, JsonNode?> entry in extra)
            {
                if (entry.Key == "rhino" || entry.Value is not JsonNode node)
                    continue;
                servers[entry.Key] = node.DeepClone();
            }
        }

        string mcpConfig = new JsonObject { ["mcpServers"] = servers }.ToJsonString(McpSerializer.Options);

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--input-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");            // required for stream-json under --print
        psi.ArgumentList.Add("--mcp-config");
        psi.ArgumentList.Add(mcpConfig);
        psi.ArgumentList.Add("--strict-mcp-config");
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("mcp__rhino*");
        psi.ArgumentList.Add("--append-system-prompt");
        psi.ArgumentList.Add(ComposedSystemPrompt);
        psi.ArgumentList.Add("--disable-slash-commands");
        psi.ArgumentList.Add(Started ? "--resume" : "--session-id");
        psi.ArgumentList.Add(SessionId.ToString());

        // Built-in defaults set Model="" / ExtraArgs=[], so these append nothing and the
        // launch is byte-identical to before; custom entries layer their model/args on top.
        if (Definition.Model.Length > 0)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(Definition.Model);
        }
        AppendExtraArgs(psi);
    }

    protected override string FormatUserMessage(UserMessage message)
    {
        // The agent has no filesystem access, so every attachment is delivered inline. Images use
        // Claude's content-block image shape ({type:image, source:{type:base64, media_type, data}});
        // text files are inlined as fenced text blocks naming the file.
        List<object> content = [];
        if (message.Text.Length > 0)
            content.Add(new { type = "text", text = message.Text });

        foreach (Attachment attachment in message.Attachments)
        {
            content.Add(attachment.Kind switch
            {
                AttachmentKind.Image => new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = attachment.MediaType,
                        data = Convert.ToBase64String(attachment.Data),
                    },
                },
                AttachmentKind.TextFile => (object)new
                {
                    type = "text",
                    text = $"```{attachment.Name}\n{Encoding.UTF8.GetString(attachment.Data)}\n```",
                },
                _ => new { type = "text", text = string.Empty },
            });
        }

        return JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content },
        }, McpSerializer.Options);
    }

    protected override void Handle(string line, Process proc)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("type", out JsonElement typeEl))
            return;

        switch (typeEl.GetString())
        {
            case "system":
                if (root.TryGetProperty("subtype", out JsonElement sub) && sub.GetString() == "init")
                    EmitSessionStarted();
                break;
            case "assistant":
                EchoAssistant(root);
                break;
            case "result":
                if (root.TryGetProperty("result", out JsonElement res) && res.ValueKind is JsonValueKind.String)
                    EmitResult(res.GetString());
                CompleteTurn(proc);   // any `result` ends the current turn
                break;
        }
    }

    void EchoAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out JsonElement msg) ||
            !msg.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind is not JsonValueKind.Array)
            return;

        foreach (JsonElement block in content.EnumerateArray())
        {
            string? type = block.TryGetProperty("type", out JsonElement bt) ? bt.GetString() : null;
            switch (type)
            {
                case "text":
                    if (block.TryGetProperty("text", out JsonElement t))
                        EmitAssistantText(t.GetString());
                    break;
                case "tool_use":
                    if (block.TryGetProperty("name", out JsonElement n))
                        EmitToolUse(n.GetString());
                    break;
            }
        }
    }
}
