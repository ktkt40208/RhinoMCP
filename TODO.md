# TODO — Rhino 8 line (`ktkt40208/RhinoMCP`)

Continuation checklist for another session / PC. **Setup, build, codegen rules, and upstream-sync:
see [`DEVELOPMENT.md`](./DEVELOPMENT.md).** Companion repo: `ktkt40208/rhinomcp-r7` (the Rhino 7
line, which is the one being exercised first since only Rhino 7 is on hand). Strategy background
lives in the "RhinoMCP fork" note in Notion (not in-repo).

_Status as of 2026-06-17. `main` is the working line._

## Done

- [x] Ported 5 additive MCP tools from jingcheng-chen/rhinomcp's surface (new files only, no plumbing edits):
      `get_document_summary`, `get_object_info`, `create_object`, `create_objects`, `modify_object`
      (+ shared `rhino/plugin/GeometryFactory.cs`, kept outside `Tools/`).
- [x] Verified on macOS arm64: full **plugin + router (NativeAOT) + ASP.NET** build, 0 errors; the
      router source generator proxies each new tool correctly (incl. open-object args).

## Verify loop (no Rhino needed)

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"   # SDK must be >= 8.0.4xx (see DEVELOPMENT.md)
dotnet build -p:RhinoTarget=R8 rhino/plugin/RhMcp.csproj
# inspect a generated proxy if needed:
#   dotnet build -p:RhinoTarget=R8 -p:EmitCompilerGeneratedFiles=true rhino/router/Router.csproj
#   → rhino/router/obj/R8/Debug/net8.0/generated/.../RouterToolProxies.g.cs
```

## Pending — needs Rhino 8 (only Rhino 7 is on hand right now)

- [ ] Runtime smoke test: call the 5 tools in Rhino 8 and confirm
      (a) `ParameterBinder` binds the open-object (`Dictionary<string,JsonElement>`) args at runtime,
      (b) the prose-only `[Description]` schema actually drives the LLM to send correct payloads.

## Pending — continuable now (build/codegen-verifiable without Rhino)

- [ ] Port more query tools: selection info, layer CRUD, attribute CRUD (source: jingcheng's tool surface).
- [ ] Port Grasshopper tools. **Arrays must be wrapped in an open-object** (codegen constraint #4):
      pass e.g. `gh_mutate_graph` operations as `request["operations"]`, not a top-level `Op[]`
      (top-level complex arrays collapse to a single object — even upstream's `g1_apply_graph` is affected).
- [ ] Optional: teach `rhino/plugin/Server/SchemaBuilder.cs` nested-object inspection — only if the
      loose open-object schema is shown (by runtime data) to cause a real LLM error rate. Gate behind
      an opt-in attribute so it doesn't override the deliberate shallow-schema default.

## Notes for whoever continues

- Follow the **four router-codegen rules** in `DEVELOPMENT.md` when adding any tool (keyword-name ban,
  single-literal `[Description]`, PassThrough-only scalar types, arrays-in-open-object).
- Keep additions to **new files** — a recent sample of upstream commits touched zero existing tool
  files, so additive new files merge cleanly when syncing `upstream` (mcneel).
- This repo is **Rhino 8+ only** (net8.0 + in-process ASP.NET Core host). For Rhino 7 use the
  `rhinomcp-r7` repo.
