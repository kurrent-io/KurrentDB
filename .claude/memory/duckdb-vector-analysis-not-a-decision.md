---
name: duckdb-vector-analysis-not-a-decision
description: "The DuckDB vector/FTS analysis is NOT a decision — don't cite it as one, and keep it out of unrelated discussions"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: a1fb8f8e-4184-4783-b96e-2f2a71d7e2be
---

Sergio corrected me firmly (2026-07-03): no decision was made about DuckDB FTS/VSS or a
float-column approach for vector search — the float-column idea was an assistant suggestion
he may not use, and DuckDB's FTS/VSS extensions are not viable. Session observations and
`src/KurrentDB.Kontext/docs/vector-fts-duckdb-analysis.md` frame this as "decided"; that
framing overstates it. What IS the direction: memories go into DuckDB as projections.

**Why:** Citing exploratory analyses (or claude-mem observations typed "decision") as
settled decisions misrepresents his position and derails the actual conversation.

**How to apply:** Treat that analysis as exploration. Never say "the 2026-07-03 decision".
Don't bring DuckDB/VSS/FTS into discussions (e.g. the MEVD vector-store abstraction in
[[mevd-usearch-connector-design]]) unless Sergio raises it himself.
