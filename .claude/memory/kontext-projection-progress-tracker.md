---
name: kontext-projection-progress-tracker
description: ProjectionProgressTracker lives in Kontext only; SecondaryIndexProgressTracker stays untouched by explicit decision — never propose unifying them
metadata: 
  node_type: memory
  type: project
  originSessionId: 1453e26a-ba04-48a0-b30e-f630e9dbff42
  modified: 2026-08-10T11:34:20.070Z
---

Decided 2026-08-10: the generic projection progress tracker (`ProjectionProgressTracker`,
`ProjectionProgressTrackerOptions`, `ProgressMark`) lives in
`src/Kontext/Kurrent.Kontext/Diagnostics/` and nowhere else. `KurrentDB.SecondaryIndexing`
keeps its own `SecondaryIndexProgressTracker` — Sérgio explicitly chose not to touch it, so
never propose consolidating, migrating, or deduplicating the two.

Settled shape: pair-shaped `ProgressMark(Position, Timestamp)` in a single DotNext
`Atomic<ProgressMark>` field (hard pair-atomicity — chosen over the original's tolerated torn
reads); head always injected via `GetHead` delegate (no self-tracking fallback); `InitProcessed`
merged into `RecordProcessed` (identical bodies once pair-shaped). Metrics
`{service}.{scope}.gap|lag|commit.seconds`. README in the Diagnostics folder is STE-checked.

Open: a project `ste100.json` glossary registering `gauge`, `scrape`, `thread`, `call`,
`return`, `log` as technical terms — Sérgio's decision, not made yet.

Related: [[kontext-kurrentdb-integration-exploration]], [[settings-objects-are-classes]]
