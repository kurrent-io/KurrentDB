---
name: sergio-perf-bar-streaming-over-batch
description: "Corpus-scale operations must be streaming/bounded-memory — batch materialization of a full corpus is unacceptable, even as a one-time backfill"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: a4527688-c8ec-4410-a7ec-ec3e0eae3e14
  modified: 2026-07-21T21:10:21.116Z
---

Sérgio ruled mikoshi's P3 result (3.13 GB peak RSS ≈ 2.8× corpus on a one-time batch scan)
UNACCEPTABLE and demanded "zero allocations and high perf" (2026-07-21).

**Why:** a one-time cost being "tolerable" doesn't meet his bar; anything that scales with corpus
size in memory is a design smell to him regardless of how rarely it runs.

**How to apply:** for any operation over corpus-scale data (session imports, scans, migrations,
projections) design for peak memory O(largest item), never O(corpus): streaming/callback APIs over
batch Vecs, zero-copy boundaries (UTF-8 span parsing, no UTF-16 blow-ups, length-prefixed native
buffers), and never transfer/serialize bytes the consumer immediately drops. Present batch-shaped
designs only as explicitly-labeled non-default conveniences. Related: [[kontext-reloaded-canonical-model]].
