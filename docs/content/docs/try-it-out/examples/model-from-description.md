---
title: Model from a description
weight: 10
prev: docs/try-it-out/examples
author: Callum
editor: SteveF
keywords:
  - modeling
  - parametric
  - prompt example
---

Describe what you want and let the assistant build it. Good for first
drafts, study models, and anything where the spec is in your head.

## Try this prompt

{{< prompt >}}
Make a parametric park bench, 1.6m long, with five wooden slats. Cast-iron
legs at each end. Put it on a layer called `Furniture` and apply
appropriate materials.
{{< /prompt >}}

## What you should see

The assistant will create the slats, legs, and supports in your active
Rhino window, place them on the right layer, and assign materials. Tweak
by asking follow-ups: "make the slats 30mm thicker", "splay the
legs outward".

## More like this

<blockquote class="page-note">
See the <a href="../../recipes#modeling">Modeling</a> section of the recipes for more starting points.
</blockquote>
