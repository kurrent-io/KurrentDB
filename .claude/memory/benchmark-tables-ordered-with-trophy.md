---
name: benchmark-tables-ordered-with-trophy
description: "Sérgio's directive 2026-08-16 — benchmark/comparison result tables always sort most→least effective and the winner gets a trophy emoji"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d2c6fd63-c002-48d7-bc13-c87c234a1875
  modified: 2026-08-16T19:31:31.048Z
---

Every benchmark or comparison results table is sorted from most effective to least effective
on the table's headline metric, and the winning row carries a trophy emoji (🏆). This
overrides the no-emoji voice default — his explicit instruction ("always order... put a
trophy emoji to the winner").

**Why:** he reads result tables top-down for the verdict; unsorted tables make him hunt.

**How to apply:** before emitting any measured-comparison table, sort by the mission metric
(recall/cosine/etc.), put 🏆 on the winner's row, keep secondary metrics as columns.
