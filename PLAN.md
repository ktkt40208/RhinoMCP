# callum/acp — In-Rhino Agent Platform

## Context

The branch has the skeleton of an **in-Rhino agent experience**: the user drives an AI agent
(Claude, Codex) from inside Rhino, and that agent loops back through Rhino's own MCP HTTP server to
edit the model. The plumbing works (`CliAgent` stream-json adapters, `AgentHost`, `AskUserTool`,
auto-started per-doc MCP server), but every user-facing surface is a stub: `RhMcpPanel` is an empty
label, `AISettingsDialog` has empty tabs, and the captured `Conversation` is never shown or saved.
This plan turns that skeleton into a product.

## Decisions (locked with the user)

- **Two synced surfaces.** A chat **panel** and the **command line** are two views on **one active
  agent per document** + one shared `Conversation`. Type in either → both update.
- **Keep CLI stream-json adapters; no user-facing "method" field.** Agents = built-in adapters
  (Claude, Codex) + optional **custom entries** that alias one built-in ("based on Claude/Codex") at
  a different path/config. Built-ins are editable but **not removable**; custom entries removable.
- **Auto-discovery at plugin load.** Probe each agent's search paths; found → enabled, missing →
  disabled. Active = first enabled+available in the chain; **Claude is the default / first.**
- **Claude-first.** Codex stays best-effort (it's full of `// verify:` flags + no inline images);
  nail its real CLI contract later.
- **Per-agent model is settings-only**, shown read-only in the panel header.
- **Global tool hide-list, in-Rhino agents only** — via a separate filtered MCP endpoint; external
  clients (Claude Desktop via the router) keep every tool.
- **External MCP servers** = one JSON textarea, merged into each agent's own MCP config beside
  `rhino`.
- **Prompt box** accepts pasted text + images + a `+` file picker. Attachments delivered **inline**
  (image → base64 block; text file → inlined contents — the agent has no filesystem access).
- **`ask_user`** renders **inline in the panel** (radios/checkboxes + Other + Cancel) **and** on the
  command line; **first answer wins** — requires a **non-blocking rework** of the tool.
- **Storage:** agent config AND conversation history both live in the plugin's **PersistentSettings**
  (history browsable via the panel's **Prev Convos**).
- **Settings = 3 tabs:** **AI Agents** (list + per-row default star + chain order + add/remove custom
  + discovery status + model/paths/args), **MCP Servers** (just the external-servers JSON textarea),
  **Tools** (global hide-list). No port field — the port stays auto-assigned (`GetNextPort`).

## Panel layout (from the sketch)

```
┌─ AI panel (per-doc) ───────────────────────┐
│ ⚙  [ claude ▾ ]  model: …      [Prev Convos]│  header: agent picker + model (ro) + history
│ ──────────────────────────────────────────  │
│ > make a box                                │
│   ⚙ add_curve …    done ✓                   │  transcript (Conversation, live)
│   ┌ ask_user: pick a unit ───────────────┐ │
│   │ ○ mm  ○ cm  ○ m  ○ Other   [Cancel]  │ │  inline ask_user (also on cmd line; 1st wins)
│   └────────────────────────────────────────┘ │
│ ──────────────────────────────────────────  │
│ [+] type a prompt…  (paste image/text)  [↵] │  prompt box: + = attach; accepts paste
└──────────────────────────────────────────────┘
```

---

## Execution model — one agent set per part

Every part below is built by a **4-agent set**:

1. **architect** — turns the part's bullet into a concrete file-level design (signatures, touch
   points, edge cases). Read-only.
2. **maker** — implements the design. **Done-criteria includes a green `dotnet build`.**
3. **reviewer** — adversarial check: did it meet the part's intent, build clean, and not regress
   sync / threading / the `IAgent` abstraction? Read-only; produces findings.
4. **fixer** — applies the findings. Then **reviewer↔fixer loops until no findings** (cap ~3 rounds).

**Sequencing:** the **spine (Part 0) runs first and alone** (everything depends on it). After it,
parts run **in parallel where safe**, each set in its own git worktree to avoid file collisions,
merged on green build:
- Wave A (parallel): **Part 1 Settings** (priority deliverable), Part 3 Tool-hiding, Part 4 Adapters.
- Part 2 Panel can run alongside Wave A.
- Wave B (after Part 2): Part 5 ask_user, Part 6 History (both need the panel).

Driven via the Workflow tool when implementation starts.

---

## Spine (Part 0 — everything hangs off this)

- **`AgentDefinition`** (dumb record): `Name`, `AgentAdapter Adapter` (Claude|Codex — surfaced only
  for custom entries), `Command`, `SearchPaths`, `Model`, `ExtraArgs`, `SystemPrompt` (a default
  prompt included on every run), `Enabled`, `IsBuiltin`.
- **`AgentFactory.Create(def)`** → `Adapter switch { Claude => new ClaudeCliAgent(def), Codex => new CodexCliAgent(def) }`. Custom entries reuse the same adapter classes.
- **Refactor `CliAgent`** (`rhino/plugin/ACP/Agents/CliAgent.cs`) to take a definition:
  `Name`/`CommandFileName` from `def`; `TryResolveCommand` (CliAgent.cs:255) iterates
  `def.SearchPaths`; `ConfigureArguments` appends `Model`+`ExtraArgs`. Seed today's hardcoded
  paths/flags as the built-in defaults (behavior unchanged out of the box).
- **`AgentRegistry`** (new): seeds built-ins, loads custom from settings, runs **discovery** (probe
  paths → `Available`), exposes the chain + `ResolveActive()` (first Enabled&&Available, Claude
  first). Re-run on load and on settings change.
- **`AgentHost`** (`rhino/plugin/ACP/Agents/AgentHost.cs`): keep the `(doc,name)` pool;
  add `ActiveName` per doc + `SetActive(doc,name)`; `For(doc)` returns the active agent.
- **Attachments + dispatch:** `record Attachment(AttachmentKind Kind, string Name, string MediaType, byte[] Data)`, `record UserMessage(string Text, IReadOnlyList<Attachment> Attachments)`. `IAgent.PromptAsync` (`rhino/plugin/ACP/Agents/IAgent.cs:12`) takes `UserMessage`. Extract **`AgentDispatch.PromptActive(doc, UserMessage)`** — the one funnel that `AgentCommand` (rhino/plugin/ACP/Commands/AgentCommand.cs:18), `CommandInterceptor` (rhino/plugin/ACP/CommandInterceptor.cs:84), and the panel all call (this is what makes the surfaces sync).
- **AISettings** (`rhino/plugin/AISettings.cs`): `GetAgents/SetAgents` (re-seed
  built-ins; list order = chain), `DefaultAgentName`, `DisabledTools`, `ExtraMcpServersJson`, and a
  **Conversations** child node (per-session JSON).

## Part 1 — Settings dialog (first user-facing build)

**File:** `rhino/plugin/UI/AISettingsDialog.cs` (empty tab bodies at 45-49).

- **AI Agents tab** — list of built-ins + custom, each row showing discovery status (✓/✗) and a
  **default star/radio**. Edit search paths, model, extra args, **default prompt** (`SystemPrompt`),
  enabled; reorder = chain; add/remove custom entries (with an "based on Claude/Codex" picker).
  Built-ins not removable.
- **MCP Servers tab** — a single validated multiline **JSON textarea** bound to
  `ExtraMcpServersJson` (`{"mcpServers":{…}}`), with help text.
- **Tools tab** — checklist of every Rhino tool, **all checked (on) by default** (opt-out):
  unchecking adds to `DisabledTools` (empty default = everything visible). Tool names via reflecting
  `[McpServerTool]` methods (same scan `ToolRegistry.Scan` does, name-only).

## Part 2 — Chat panel + sync

**Files:** `rhino/plugin/ACP/RhMcpPanel.cs`, `rhino/plugin/ACP/Agents/Conversation.cs`

- Add `event Action? Changed;` to `Conversation`, raised inside the locked mutators. Extend the
  `ToolUse` event to carry the tool's **args** (and capture **tool results**) so chips can expand —
  the Claude adapter (Part 4) already sees `tool_use.input`; results come back on the stream's
  `user`/`tool_result` messages.
- Build `RhMcpPanel` (Eto, `PerDoc` at `rhino/plugin/Plugin.cs:19`) per the sketch:
  header (agent dropdown listing **all agents, missing ones greyed** with a "not found" hint +
  read-only model + **Prev Convos dropdown of recents** + **New conversation** + Settings gear),
  **chat-bubble** transcript (user/agent bubbles; tool calls as compact chips that **click to expand**
  args/result) from the active `Conversation`, prompt box with `+` file picker + paste of
  image/text. Attachments show as **removable chips/thumbnails** before send. **Enter sends,
  Shift+Enter = newline.** The send button toggles to a **Stop** button while a turn runs (in the
  prompt box). Sending builds a `UserMessage` → `AgentDispatch.PromptActive`.
- **New conversation** persists the current `Conversation` to history and starts a fresh session id.
  **No-agent state:** when discovery finds nothing, show a message + button to open AI Settings.
- Re-render on `Changed`, **marshaled via `RhinoApp.InvokeOnUiThread`** (reader loop is off-thread;
  Eto is UI-thread-only). Resubscribe on agent switch.

## Part 3 — In-Rhino-only tool hiding

**Files:** `rhino/plugin/McpServer.cs`, `rhino/plugin/Server/McpEndpoint.cs`, `AgentDispatch`

- Map a **second filtered endpoint**: `App.MapMcp("/")` (external) + `App.MapMcp("/agent")`
  (filtered). Add `bool filtered` to `MapMcp`→`McpDispatcher`. When filtered, `HandleToolsList`
  (McpEndpoint.cs:144) excludes `DisabledTools` and `HandleToolCallAsync` (McpEndpoint.cs:205)
  rejects them; read the set **live per request**. In-Rhino agents use `/agent`.

## Part 4 — Adapters: external MCP merge + attachment delivery (Claude-first)

**Files:** `rhino/plugin/ACP/Agents/ClaudeCliAgent.cs`, `rhino/plugin/ACP/Agents/CodexCliAgent.cs`

- Claude merges `ExtraMcpServersJson` into `--mcp-config` (ClaudeCliAgent.cs:20); Codex translates to
  `-c mcp_servers.<name>.*` (best-effort).
- Append the agent's `SystemPrompt` to the launch (Claude `--append-system-prompt`, beside the
  existing `AskUserSteer` at CliAgent.cs:49; Codex best-effort).
- `FormatUserMessage(UserMessage)`: Claude builds content blocks (text + image base64 + text-file
  contents). Codex degrades (plain text + inlined text files; images → a short note).

## Part 5 — `ask_user` dual-channel rework (inline panel + CLI, first wins)

**File:** `rhino/plugin/Tools/AskUserTool.cs` (currently a blocking `GetOption`)

- Make it **non-blocking** (`[BackgroundThread]`): register a **pending question** per doc, `await` a
  `TaskCompletionSource`. Panel renders inline options **in the transcript flow** (radio/checkbox +
  Other + Cancel) → click completes it; command line prints options and `CommandInterceptor`
  interprets the next `"…"` entry as the answer. **First wins**, other dismissed. Same
  `{ selected, cancelled }` return shape.

## Part 6 — History persistence + Prev Convos

**New:** `ConversationStore`, transcript DTOs

- DTOs: `ConversationDto(SessionId, AgentName, DocTitle, StartedAt, Turns)`, `TurnDto`,
  `TurnEventDto`. `ConversationStore` reads/writes the **PersistentSettings** Conversations node,
  rewriting on `CompleteTurn`. Pass agent name + doc title into `Conversation` at construction.
- **Prev Convos** (panel header **dropdown of recents**): selecting one loads a read-only transcript
  view (Back → live). Optional type-to-filter + prune-oldest-N. No resume in v1.

---

## Order & reuse

**Order:** Spine → Settings → Panel → Tool hiding → Adapters → ask_user → History.
**Reuse:** `AgentHost` cleanup; `RhinoMcpHost.GetNextPort`/`StartOrRestart`; `CliAgent` `AskUserSteer`
+ `Emit*`; `Conversation.Render`; `McpSerializer.Options`; `ToolRegistry.Scan` for tool names;
`MapMcp`; keep the `IAgent` abstraction.

## Risks

- **ask_user rework** (Part 5): a modal `GetOption` freezes the UI thread; must become an async
  pending-question model with two channels racing. Main design risk.
- **Eto UI-thread marshaling** for all panel updates; verify clipboard/image-paste on macOS.
- **Codex** unverified flags + no inline images — Claude is the proven path.
- **PersistentSettings for transcripts** (user's choice): watch for bloat → prune safeguard.
- **Filtered endpoint vs router:** confirm `rhino/router/` targets `/`.

## Verification

1. **Build:** `dotnet build rhino/plugin/RhMcp.csproj`.
2. **Settings:** add/edit a custom agent, star a default, edit MCP JSON, untick a tool.
3. **Discovery/chain:** move the `claude` binary out of its path → ✗/disabled, active falls through
   the chain; restore → Claude active.
4. **Panel/sync:** prompt from the panel → streams in panel; type `"make a box"` at the command line
   → same conversation updates. Switch agent → swaps; switch back → transcript intact.
5. **Attachments:** paste an image + attach a text file → Claude receives them.
6. **Tool hiding:** unticked tool absent from the in-Rhino `tools/list` but present for an external
   client on `/`.
7. **ask_user:** options appear inline in the panel AND on the command line; answering one dismisses
   the other.
8. **History:** run several conversations, reopen Prev Convos, confirm persistence across restart +
   search.
