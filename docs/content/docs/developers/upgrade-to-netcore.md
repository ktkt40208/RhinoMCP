---
title: Upgrade a plugin to .NET Core
linkTitle: Upgrade to .NET Core
weight: 1
prev: docs/developers
author: Callum
editor: SteveF
keywords:
  - .NET Core
  - plugin upgrade
  - net48
  - Claude Code
---

// USE https://github.com/sbaer/selcommands as an example

If you have a Rhino plugin still targeting `net45` or `net48`, you'll want to move it to `net8.0` (Rhino 8) or `net10.0` (Rhino 9 WIP). This page showcases how you can use the RhinoMCP to makes this upgrade seamless.

## What you need

- **Claude Code** with the [Rhino MCP plugin](../getting-started/cc-plugin) installed.
- **Rhino** open with Rhino MCP running, on the version you're targeting.
- Your plugin's source checked out locally, with Claude Code started in
  that repo.

## The loop

With Rhino MCP loaded, the assistant can:

- Read your `.csproj` and source files.
- Edit project and code files and re-build.
- Load the freshly built `.rhp` into Rhino, run its commands, and read back
  what happened, so it can tell whether a change actually worked, not
  just whether it compiled.

That last point is what makes this worth doing through Rhino MCP rather than
a plain LLM session: the assistant closes its own feedback loop instead of
asking you to copy errors back and forth.

## A prompt to start with

{{< prompt >}}
Upgrade this legacy RhinoCommon plugin to a modern Rhino 8 plugin per the
McNeel CSRhino template:
https://github.com/mcneel/RhinoVisualStudioExtensions/tree/main/Rhino.Templates/content/CSRhino

- SDK-style csproj, TargetFrameworks=net8.0;net48, EnableDynamicLoading,
  TargetExt=.rhp, RhinoCommon via NuGet (ExcludeAssets="runtime")
- Move Title/Company/Description/Version into the csproj; keep PlugInDescription
  attrs and the original Guid in AssemblyInfo.cs
- Add .vscode/launch.json + tasks.json from the template, Rhino 8 only
  (netcore Mac+Win, netfx Win), pointed at this project
- Don't touch command sources unless an API changed
- `dotnet build` to verify both TFMs
{{< /prompt >}}

Adjust the target framework and "show me the diff" cadence to taste.

## What to review

Even with the assistant driving, you're still the one merging the result.
Things worth looking at before you accept the work:

- **The `.csproj` diff.** Multi-targeting introduces conditional package
  references and conditional `Compile` items; make sure the conditions
  match how you want the two TFMs to differ.
- **Any swapped APIs.** When a `net48`-era RhinoCommon call doesn't exist
  on the newer target, the assistant will pick a replacement. Spot-check
  the substitutions: same behavior, not just same signature.
- **Plugin manifest / yak files.** If you ship through yak, the manifest
  and target folders may need updating too.

## When the assistant gets stuck

If it loops on the same error or starts inventing APIs, stop it and either
narrow the scope (one file, one error) or paste the actual RhinoCommon
docs / NuGet version it should be working against. The MCP gives it eyes
inside Rhino, but it can't read your minds about which target framework
version you actually want.
