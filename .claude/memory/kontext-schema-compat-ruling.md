---
name: kontext-schema-compat-ruling
description: "Kontext migrations = one append-only MigrationStep stream (DbUp-style), per-step history journal, ForceRebuild catalog sweep; no modules, no drift probe; current dev data rebuildable at will"
metadata: 
  node_type: memory
  type: project
  originSessionId: 338d2add-b3f4-4cc4-b0cc-1543efb7fc8e
  modified: 2026-08-13T19:22:50.582Z
---

Settled 2026-08-13 after four design iterations in conversation. The Kontext schema machinery (SHIPPED at `src/Kontext/Kurrent.Kontext/Infrastructure/Data/Migrations/`) is a DbUp-shaped step stream, NOT the earlier module-based design:

- `ISchemaMigrationStep` — Version + Name (defaults to class name) + Type + ExecuteAsync(executor, ct); one class per step, one file per class, DI-registered; the manager SORTS by Version so registration order is irrelevant. Explicit int Version IS the order (Sérgio rejected name-convention ordering). The earlier `MigrationStep` record is deleted — Sérgio wanted implementable classes in a folder.
- `MigrationStepType` — RunOnce (default; frozen body, skipped once recorded) vs RunAlways (reasserts every boot, body kept current — DbUp's views/macros shape; executions ARE journaled, unlike DbUp). Deliberate DbUp borrowing per Sérgio.
- `schema_migrations` journal — one row per executed step: version, name, executed_at (epoch ms), duration_ms. Highest version = store state. Constraint-free, unqualified name (executor's connection decides the catalog).
- `DuckDBSchemaManager` — validates ascending/unique at construction, logs the plan at Debug, each step at Information with duration, fail-fast, downgrade = throw.
- `ForceRebuild` option — self-authorizing; drops every table in the active catalog via duckdb_tables() enumeration (history falls too) and replays from zero.
- Schema DRIFT (dimension/model change) is the STEP AUTHORS' responsibility, not the lib's — Sérgio explicitly removed the drift probe and the AllowDestructiveRebuild gate. Do not reintroduce modules, drift probes, or per-module versioning.
- Current dev schemas/data carry zero compat obligation — rebuildable at will.

**Why:** Sérgio wanted DbUp's mental model — "a list of migration steps, always incrementing, no module-targeted thing" — and pushed each abstraction out until only execute-in-order + record + report remained.

**How to apply:** New schema changes = append one frozen step. Wiring/retrofit of KontextSchema/KontextRecordsSchema (they keep only maintenance ops: vector index lifecycle, compact, vacuum) pending Sérgio's review. Open flag: DROP TABLE through the lance extension unprobed. Related: [[kontext-kurrentdb-integration-exploration]], [[kontext-reloaded-canonical-model]], [[discuss-before-recording]].
