using System.Diagnostics;

namespace RhMcp;

// CliAgent over Claude Code's stream-json stdio mode.
internal sealed class ClaudeCliAgent : CliAgent
{
    public override string Name => "claude";

    protected override string CommandFileName => "claude";

    protected override string NotFoundMessage =>
        "Claude CLI not found. Install Claude Code (claude.ai/install).";

    protected override void ConfigureArguments(ProcessStartInfo psi, string mcpUrl)
    {
        // Same {"mcpServers":{...}} shape Claude Code expects; this is the agent's hands.
        string mcpConfig = $$"""
            {
              "mcpServers": {
                "rhino": { "type": "http", "url": "{{mcpUrl}}" }
              }
            }
            """;

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--input-format");
        psi.ArgumentList.Add("stream-json");          // long-lived: turns arrive on stdin
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");            // required for stream-json under --print
        psi.ArgumentList.Add("--mcp-config");
        psi.ArgumentList.Add(mcpConfig);
        psi.ArgumentList.Add("--strict-mcp-config");  // ignore the user's other MCP servers
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("mcp__rhino*");          // only Rhino MCP tools; Bash/Edit/etc. denied
        psi.ArgumentList.Add("--disable-slash-commands");
        psi.ArgumentList.Add(Started ? "--resume" : "--session-id");
        psi.ArgumentList.Add(SessionId.ToString());
    }

    protected override string FormatUserMessage(string text) =>
        JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content = new object[] { new { type = "text", text } } },
        }, McpSerializer.Options);

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
                    RhinoApp.WriteLine($"[{Name}] session started.");
                break;
            case "assistant":
                EchoAssistant(root);
                break;
            case "result":
                if (root.TryGetProperty("result", out JsonElement res) && res.ValueKind is JsonValueKind.String)
                    RhinoApp.WriteLine($"\n[{Name}] {res.GetString()}");
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
                        RhinoApp.Write(t.GetString());
                    break;
                case "tool_use":
                    if (block.TryGetProperty("name", out JsonElement n))
                        RhinoApp.WriteLine($"\n[{Name}] ⚙ {n.GetString()}");
                    break;
            }
        }
    }
}
