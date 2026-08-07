---
name: log-position-not-tfpos
description: "Kontext forward designs use the single log position (commit position), never TFPos"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: a1fb8f8e-4184-4783-b96e-2f2a71d7e2be
---

Sergio (2026-07-03): `TFPos` (prepare/commit pair) was a mistake by the original authors —
"there is only the log position". All forward-looking Kontext design and new code uses the
single **log position, i.e. the commit position**, for watermarks/checkpoints (e.g. the
vector store durability watermark). Existing engine code still takes `TFPos`; describe it
factually when reading, but never carry it into new APIs or designs.

**Why:** The prepare/commit distinction is noise for these consumers; the commit position is
the meaningful ordering key.

**How to apply:** In new Kontext interfaces, records, and docs, model position as a single
`long` log position (commit). Related: [[duckdb-vector-analysis-not-a-decision]].
