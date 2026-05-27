---
title: Upgrade a GH1 script to GH2
linkTitle: Upgrade GH1 to GH2
weight: 1
prev: docs/advanced
author: Callum
editor: SteveF
keywords:
  - Grasshopper
  - GH1 to GH2
  - .gh definition
  - upgrade
---

Rhino 9 ships Grasshopper 2 alongside Grasshopper 1. If you have a GH1 definition you'd like to upgrade, your AI Agent and the MCP can read your GH1 graph and rebuild it on a GH2 canvas, solving both side-by-side to confirm they match.

This is different from [upgrading a plugin's compiled components](../developers/upgrade-gh1-to-gh2); here you're porting a `.gh` definition, not source code.

## A prompt to start with

{{< prompt >}}
Using RhinoWIP, open this GH1 script \<path> and rebuild the same definition on a GH2 canvas. Use the same sample inputs on both, solve them, and tell me whether the outputs match. If anything doesn’t have a clean GH2 equivalent, stop and ask before substituting.
{{< /prompt >}}

## What you should see

The assistant opens Rhino 9 WIP, Grasshopper 1 and 2. It will then remake your GH1 script in GH2. It should solve both canvases and compare the outputs. You can swap slider values on either side and check that they still agree.

## What to review

- **Component substitutions.** GH2 component names and parameter types don't always line up one-to-one. Have the assistant flag any case where it picked the closest match rather than an exact equivalent.
- **Third-party components.** If your GH1 definition relies on a plugin that doesn't have a GH2 build yet, the assistant should surface that and skip those nodes rather than fake them.
- **Parity vs Intent** some components work differently in GH1 and GH2, so the script may be technically identical, but output slightly differently.

## When the assistant gets stuck

If part of the definition can't be cleanly ported, ask for a short "these need human eyes" list at the end instead of stubbed nodes that silently solve to nothing. A flagged gap is more useful than a quiet mismatch.
