# DEVELOPMENT — fork notes (Rhino 8 line)

This is a personal fork of **mcneel/RhinoMCP**. It is the **Rhino 8** line of a two-repo setup:

| Line | Repo | Base | Target |
|------|------|------|--------|
| **Rhino 8** (this repo) | `ktkt40208/RhinoMCP` | mcneel/RhinoMCP | net8.0 / Rhino 8+ |
| **Rhino 7** | `ktkt40208/rhinomcp-r7` | jingcheng-chen/rhinomcp | net48 / Rhino 7 |

Why two repos: mcneel/RhinoMCP is net8.0 + an in-process ASP.NET Core host and only
targets Rhino 8/9 — it cannot run on Rhino 7. For Rhino 7 we retarget the lighter
jingcheng-chen/rhinomcp plugin instead (see that repo's `DEVELOPMENT.md`).

Operating posture: **pull-only fork** — pull upstream changes in; do not expect to upstream
changes back (mcneel does not merge external PRs). Keep added tools in new files to minimise
merge friction (a recent sample of upstream commits touched zero existing tool files).

## Where the work is

The added MCP tools are on **`main`** (this fork's working line; `main` tracks our work,
not a pristine mirror of upstream — see "Syncing upstream" below):

- `rhino/plugin/Tools/GetDocumentSummaryTool.cs` — `get_document_summary`
- `rhino/plugin/Tools/GetObjectInfoTool.cs` — `get_object_info`
- `rhino/plugin/Tools/CreateObjectTool.cs` — `create_object` (typed geometry)
- `rhino/plugin/Tools/CreateObjectsTool.cs` — `create_objects` (batch)
- `rhino/plugin/Tools/ModifyObjectTool.cs` — `modify_object`
- `rhino/plugin/GeometryFactory.cs` — shared geometry builder (kept **outside** `Tools/` so the router codegen never treats it as a tool)

All are ported from jingcheng-chen/rhinomcp's tool surface as **purely additive** new files
(no edits to the MCP plumbing). They build but have **not** been runtime-tested in Rhino yet.

## Build (macOS, Apple Silicon)

The router uses a Roslyn **source generator** that requires Roslyn **≥ 4.11**, which ships in
.NET SDK **8.0.4xx+**. A too-old SDK (e.g. Homebrew's `dotnet@8` = 8.0.128 / Roslyn 4.8) makes
the router fail to compile (`CS0234: 'Generated' does not exist`). Install a current 8.0 SDK
without sudo:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --quality GA --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"   # use this SDK, not an older one
dotnet --version    # expect 8.0.4xx

dotnet build -p:RhinoTarget=R8 rhino/plugin/RhMcp.csproj
```

A full build produces `rhino/plugin/bin/R8-osx/Debug/net8.0/RhinoMcpPlatform.rhp` plus the
bundled NativeAOT router (osx-arm64) and a pruned ASP.NET Core runtime. macOS support is
**Apple Silicon only** (no osx-x64 router). VS Code "Run and Debug" / the `build (R8)` task do
the same. The `tests/ngentic` submodule is only needed for the agent-driven tests, not the
plugin build.

## Adding a tool — router-codegen rules (learned the hard way)

A tool is one `[McpServerToolType]` class with a static `[McpServerTool]` method under
`rhino/plugin/Tools/`. The router proxy/schema/registration are generated at build time. Four
constraints the generator imposes (verified by inspecting the generated `RouterToolProxies.g.cs`):

1. **Parameter names are emitted verbatim** — never use a C# keyword name like `@params`
   (produces an illegal proxy). Use a plain identifier such as `parameters`.
2. **`[Description]` must be a single string literal** — concatenated literals (`"a" + "b"`)
   are dropped, leaving an empty description on the router proxy. (PR #61 upstream adds concat
   support; until merged, single literal only.)
3. **Only specific types pass through as-is** (`string`, `string?`, `string[]`, `int[]`, `bool`,
   `int`, `long`, `double`, `float` and scalar `?` forms). Everything else — incl. `double[]`,
   `int[]?`, and any complex/record type — collapses to a single open object
   `Dictionary<string, JsonElement>?`. So pass vectors/colours as a hex string or inside an
   open-object dict, not as `double[]`.
4. **Top-level arrays of complex types cannot be passed as arrays** — they also collapse to a
   single open object. Wrap an array as a value *inside* an open-object dict instead (e.g.
   `request["objects"] = [...]`); nested JSON round-trips faithfully. Even upstream's own
   `g1_apply_graph` is affected by this.

`create_object` (open-object `parameters`), `create_objects` (array wrapped in `request`), and
`modify_object` (vectors in an open-object `transform`) are worked examples of rules 3–4.

## Run

Build the yak / load the plugin in Rhino 8, then wire an MCP client per upstream docs
(`https://mcneel.github.io/RhinoMCP/docs/`). The router is the stdio entry point; it spawns/adopts
Rhino instances (`spawn_slot`/`close_slot`/`list_slots`) and proxies every plugin tool with a
trailing `slot` argument.

## Syncing upstream

This is a **pull-only** fork: `main` carries our work, and we never PR back to mcneel. The `upstream`
remote points at mcneel/RhinoMCP; the tag `upstream-fork-point` marks the commit `main` was forked from.

```bash
git remote -v                       # upstream -> github.com/mcneel/RhinoMCP (added already)
git fetch upstream
git merge upstream/main             # pull upstream changes into our main
git diff upstream-fork-point..main  # everything this fork has changed since the fork point
```

Conflicts are rare because our tools are additive new files (a recent sample of upstream commits
touched zero existing tool files).

## Status / next

- Build + router codegen + "purely additive" porting: **verified on macOS arm64.**
- **Not yet done:** runtime smoke test inside Rhino 8 (call `get_document_summary` etc. and
  confirm `ParameterBinder` binds the open-object args at runtime).
- Next ports: selection/layer queries, then Grasshopper tools (use rule 4 for `gh_*` arrays).
