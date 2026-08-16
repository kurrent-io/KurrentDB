---
name: diagrams-always-termaid
description: "Sérgio's directive 2026-08-15 — every diagram renders through the termaid skill; never hand-draw box art"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d2c6fd63-c002-48d7-bc13-c87c234a1875
  modified: 2026-08-15T20:58:04.873Z
---

Every diagram in a reply goes through the `termaid` skill (Mermaid → `scripts/render.py
--width 100` → Unicode art in an untagged fence). Hand-drawn box-drawing is not acceptable
when termaid is installed — Sérgio called this out after a hand-drawn tokenizer pipeline.

**Why:** termaid output is aligned, compactable, and consistent; hand-drawn art drifts and
wastes effort.

**How to apply:** before emitting any flow/hierarchy/sequence shape, invoke the termaid
skill and render. Wide chains: shorten labels or split into stacked renders rather than
shipping an overflow. Related: [[sergio-csharp-style-law]].
