---
title: Lance DuckDB Extension — prefilter=true Regression on Local Datasets (533e0ee → 2f167ea)
type: analysis
date: 2026-08-05
author: sergio
tags: [lance, duckdb, prefilter, regression, upstream]
scope: lance extension search functions on locally attached datasets
related: [2026-08-02-0042-lance-duckdb-containment-pushdown]
---

## Summary

The lance DuckDB extension changed the `prefilter` contract between build `533e0ee` (v1.5.3 channel)
and build `2f167ea` (v1.5.5 channel). On the new build, `prefilter := true` fails on locally attached
datasets unless the caller also passes the `filter :=` string parameter. An outer SQL `WHERE` clause
no longer satisfies the requirement. This breaks every query written against the old contract, where
`filter :=` was rejected for local datasets and `WHERE` pushdown was the documented filter mechanism.
Reproduced live with a 5-statement SQL script on DuckDB v1.5.5, no client library involved. The text
below under "Upstream report" is ready to hand to the LanceDB team.

## Findings

Probe matrix, DuckDB v1.5.5 (Variegata, `d8cdaa33fd`), lance `2f167ea`, macOS arm64, local
directory attach (`ATTACH '<dir>' AS ldb (TYPE LANCE)`):

| Probe | Query shape                                          | Result |
|-------|------------------------------------------------------|--------|
| P1    | `lance_vector_search(..., k := 2)`                   | OK — correct distances (0.0 / 2.0) |
| P2    | `... , prefilter := true`                            | ERROR: "requires explicit filter when prefilter=true on namespace-backed tables" |
| P3    | `... , prefilter := true) WHERE id = 'a'`            | ERROR — same; outer WHERE does not count |
| P4    | `... , prefilter := true, filter := 'id = ''a'''`    | OK — returns the filtered row |
| P5    | `... , prefilter := false`                           | OK |
| P6    | `lance_fts(..., prefilter := true)`                  | ERROR — same message |

Contract inversion vs the validated `533e0ee` build:

| Behavior                                | 533e0ee (old)                                    | 2f167ea (new) |
|-----------------------------------------|--------------------------------------------------|----------------|
| `filter :=` on local dataset            | REJECTED — "only supported for namespace-backed tables" (source-confirmed in `lance_search.cpp`) | ACCEPTED |
| Bare `prefilter := true`                | Legal                                            | ERROR |
| `prefilter := true` + outer `WHERE`     | Legal — predicates push down, genuine prefilter  | ERROR |
| Local dataset classification            | Not namespace-backed                             | Namespace-backed |

Impact on this codebase: 42 test failures across `Kurrent.Kontext.Tests` and `DuckLance.Tests`
(run `e99e04cbf4284f62b9fe1368a2544ca9`, 2026-08-05). All three search paths in `KontextDataStoreV2`
and the DuckLance MEVD collection use `prefilter := true` + parameter-bound `WHERE` splicing per the
old contract. One additional DuckLance capability-gate test inverted (`Expected to throw
NotSupportedException`) — consistent with `filter :=` landing for local datasets.

## Recommendations

For upstream: see the report below. For this codebase, decision pending (adapt to `2f167ea`
semantics vs pin the engine back to 1.5.3): tracked in the session discussion of 2026-08-05.

## Method

Minimal repro executed on the DuckDB v1.5.5 CLI with the stock extension from
`http://extensions.duckdb.org/v1.5.5/osx_arm64/`. Old-build behavior cited from the validated
knowledge base (`.claude/context/project/duckdb-lance.md`, entries validated 2026-07-17/20 against
`533e0ee`). Extension build identities read from `~/.duckdb/extensions/*/osx_arm64/lance.duckdb_extension.info`.

---

## Upstream report (hand-off text)

**Title: `prefilter := true` now errors on locally attached datasets unless `filter :=` is passed — outer `WHERE` no longer accepted (regression 533e0ee → 2f167ea)**

### Environment

- DuckDB v1.5.5 (Variegata, `d8cdaa33fd`), macOS arm64 (also reproduced through DuckDB.NET 1.5.5)
- lance extension build `2f167ea`, installed from `http://extensions.duckdb.org/v1.5.5/osx_arm64/`
- Last known-good: build `533e0ee` from the v1.5.3 channel
- Local directory attach — no LanceDB Cloud / REST namespace involved

### Repro

```sql
LOAD lance;
ATTACH '/tmp/lance-repro' AS ldb (TYPE LANCE);
CREATE TABLE ldb.main.docs (id VARCHAR, content VARCHAR, vec FLOAT[4]);
INSERT INTO ldb.main.docs VALUES
  ('a', 'hello world',  CAST([1.0,0.0,0.0,0.0] AS FLOAT[4])),
  ('b', 'goodbye moon', CAST([0.0,1.0,0.0,0.0] AS FLOAT[4]));

-- FAILS on 2f167ea, worked on 533e0ee:
SELECT id FROM lance_vector_search('ldb.main.docs', 'vec',
  CAST([1.0,0.0,0.0,0.0] AS FLOAT[4]), k := 2, prefilter := true);
-- Invalid Input Error: lance_vector_search requires explicit filter
-- when prefilter=true on namespace-backed tables

-- ALSO FAILS — an outer WHERE does not satisfy the check:
SELECT id FROM lance_vector_search('ldb.main.docs', 'vec',
  CAST([1.0,0.0,0.0,0.0] AS FLOAT[4]), k := 2, prefilter := true)
WHERE id = 'a';

-- WORKS — only the string parameter is accepted:
SELECT id FROM lance_vector_search('ldb.main.docs', 'vec',
  CAST([1.0,0.0,0.0,0.0] AS FLOAT[4]), k := 2, prefilter := true, filter := 'id = ''a''');
```

`lance_fts` fails the same way with `prefilter := true`.

### Expected

On `533e0ee` the contract for local datasets was the opposite, and our code was written against it:

1. `filter :=` was rejected for local datasets ("filter parameter is only supported for
   namespace-backed tables", thrown from `lance_search.cpp`).
2. The filter mechanism was a plain SQL `WHERE` around the table function. Supported predicates push
   down into the Lance scan (visible in `EXPLAIN` as "Lance Pushed Filter Parts"), and
   `prefilter := true` made them a genuine prefilter.
3. Bare `prefilter := true` with no filter at all was legal (a no-op prefilter).

### Actual

On `2f167ea`, a local directory attach is classified as namespace-backed, and `prefilter := true`
hard-errors unless the `filter :=` string parameter is present. Previously valid queries now fail.

### Why this hurts

1. **Breaking change with no migration path.** Every query using the documented
   `prefilter := true` + `WHERE` pattern fails at bind time.
2. **It demotes safe parameter binding to string interpolation.** `WHERE` predicates bind values as
   parameters (`WHERE id = $id`). The `filter :=` parameter is a string — values must be spliced
   into the filter text, which reintroduces quoting bugs and injection surface that the pushdown
   path had eliminated.
3. **Bare `prefilter := true` is a legitimate shape.** Callers set the knob explicitly and
   uniformly; when a query has no filter, prefilter is a no-op. Erroring forces callers to toggle
   the knob based on whether a filter happens to be present.
4. The check appears to run at bind time, before the optimizer pushes outer `WHERE` predicates into
   the scan — which would explain why a pushable `WHERE` is not counted. (Inference from observed
   behavior, not from source.)

### Ask

1. Restore bare `prefilter := true` as valid on local datasets (no-op when no filter exists).
2. Count optimizer-pushed `WHERE` predicates as the prefilter input, as `533e0ee` did.
3. Keep `filter :=` as an addition for local datasets (it is useful), not a requirement.
