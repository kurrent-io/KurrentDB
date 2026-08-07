---
title: lance-duckdb — array_has_any containment pushdown (feasibility + change surface)
type: research
date: 2026-08-02
author: sergio
tags: [kontext, lance, duckdb, pushdown, label-list, tags, prefilter]
---

## Question

Can tag-containment predicates (`array_has_any` / `array_has_all` / `array_has`) be made to push
down from the DuckDB extension (`lance-duckdb`) into Lance, so that Kontext's tag-filtered
vector/hybrid search prefilters upfront via the `LABEL_LIST` index — retiring the validated
oversample rule (`k` := whole table row count)?

Context: `.claude/context/project/duckdb-lance.md` §prefilter/§containment — containment is NOT
pushed down (source-confirmed: no translation in `lance_filter_ir.cpp`); equality predicates DO
push down; `prefilter := true` = genuine prefilter. Kontext's `memories` table already carries a
deliberately dormant `tags_idx` `LABEL_LIST` index
(`src/Kontext/Kurrent.Kontext/Data/KontextSchema.cs:30-38`).

## Findings

All findings verified 2026-08-01/02 by source dive. Repos read:
`~/dev/contrib/lance-duckdb` at `63c2446` ("chore: update lance dependency to v9.0.0 (#234)") —
clean clone, fast-forwarded 3 commits; `lancedb/lance` v9.0.0 (shallow scratch clone for the
Lance-side checks).

### 1. Verdict

**Contained, DuckDB-extension-only contribution. Lance already does the hard part.**
~4 files + 1 new test file, single PR. No Lance/Rust-crate-internal changes.

### 2. Pipeline map (the extension)

- Entry: `src/lance_filter_ir.cpp`. Two parallel builders bottom out in
  `src/lance_expr_ir.cpp`'s `TryEncodeLanceExprIR*` encoders, serializing a custom binary IR
  (`"LFT1"`/`"LUE1"` magic) — NOT Substrait, NOT a filter string.
  - `TryBuildLanceTableFilterIRExpr` (`lance_filter_ir.cpp:169-425`) — TableFilterSet path;
    function whitelist `lower/upper/starts_with/ends_with/contains` only (`:188-230`).
  - `TryBuildLanceExprFilterIR` (`lance_filter_ir.cpp:747-1073`) — general Expression path
    (KNN prefilter / DELETE / post-Filter); adds BETWEEN, regexp, LIKE/ILIKE (`:765-914`).
    Column refs incl. `struct_extract` (`:515-745`).
- Type gate for both: `LanceFilterIRSupportsLogicalType` (`lance_filter_ir.cpp:490`) →
  `LanceExprIRSupportsLogicalType` (`lance_expr_ir.cpp:31-57`) = {BOOLEAN, TINYINT..UBIGINT,
  FLOAT, DOUBLE, VARCHAR, DATE, TIMESTAMP(+variants), DECIMAL, STRUCT}. **No LIST/ARRAY** —
  today ANY filter on a LIST column fails this gate before function names are even considered.
- Literal wire format: `TryEncodeLanceExprIRLiteral` (`lance_expr_ir.cpp:661`, enum `:78-89`):
  NULL/BOOL/I64/U64/F32/F64/STRING/DATE32/TIMESTAMP/DECIMAL128. **No list/array literal tag.**
- Rust decode: `rust/expr_ir.rs` `parse_expr_ir_payload` (`:143`) builds a real
  `datafusion_expr::Expr`. `resolve_scalar_function` (`:399`) hardcodes only the 5 string UDFs;
  `rust/filter_ir.rs:15` (the only filter path, used by every FFI site) passes `ctx=None`, so
  the registry fallback is dead — new functions need hardcoded accessors.
- Execution: `rust/ffi/exec.rs:178` — `scan.filter_expr(filter)` on the real Lance scanner.

### 3. What Lance accepts (verified in lancedb/lance v9.0.0 source)

- The scanner takes the DataFusion `Expr` as-is; no separate filter language.
- **Lance's scalar-index planner already has full native containment support**:
  `LabelListQueryParser::visit_scalar_function`
  (`lance-index/src/scalar/expression.rs:762-825`) pattern-matches
  `array_has_any` / `array_has_all` / `array_has` (comment: DataFusion normalizes
  `array_contains` → `array_has`) into `LabelListQuery::HasAnyLabel/HasAllLabels`. This parser
  is what `LabelListIndex` returns (`lance-index/src/scalar/label_list.rs:777`).
- Requirement: `args[1]` must resolve to a literal `ScalarValue::List` via `maybe_scalar`
  (`expression.rs:793-801`) — a plan-time list constant (bound parameters fold to constants;
  scalar equivalents already validated to push down).
- **Index usage: genuine `LABEL_LIST` index query** — not fragment-stat pruning, not a
  recheck-scan. Shipped in v9.0.0, no feature gates, no TODOs.

### 4. Element types — unrestricted (non-nested scalars)

- Build path: `LabelListIndexPlugin` (`label_list.rs:677-691,726-735`) only requires the outer
  type be `List(_)/LargeList(_)`; `unnest_schema` (`:344-362`) explodes elements, then
  `train_index` (`:740-741`) hands off to `BitmapIndexPlugin::build_bitmap_index_state` —
  LABEL_LIST = "unnest + ordinary BITMAP dictionary index". Only gate in the lineage:
  reject nested element types (`bitmap.rs:1722-1726`). Strings, ints, floats, bools, dates,
  timestamps, decimals all work.
- Query path: `LabelListQueryParser` rebuilds needle elements generically
  (`ScalarValue::try_from_array`, `expression.rs:793-801`) — no element-type assumptions.
- Kontext relevance: all three array columns are `VARCHAR[]` (`tags`, `cited_memory_ids`,
  `supersedes` — `KontextSchema.cs:65-68`).

### 5. Change surface — IR route (RECOMMENDED)

1. `lance_expr_ir.cpp:31` `LanceExprIRSupportsLogicalType` — admit `LogicalTypeId::LIST`
   (decide on ARRAY).
2. `lance_expr_ir.cpp:~661` + enum `:~78` — new LIST literal tag: length-prefixed sequence of
   already-supported scalar literals (recursive reuse). The one genuinely new design piece.
3. `lance_filter_ir.cpp` — branches for `array_has_any`/`array_has_all`/`array_has` in BOTH
   builders (`~:188`, `~:765`); a `TryGetNonNullListConstant` helper mirroring
   `TryGetNonNullVarcharConstant` (`:130-167`), including non-try cast unwrapping.
4. `rust/expr_ir.rs` `parse_literal` (`~:517`) — `LIT_LIST` arm building `ScalarValue::List`
   (Arrow ListArray) from recursively parsed elements.
5. `rust/expr_ir.rs:399` `resolve_scalar_function` — hardcoded accessors for the three UDFs
   from `datafusion-functions-nested` (transitively present at v53.1.0 per Cargo.lock; promote
   to explicit Cargo.toml dependency; exact accessor fn names = 2-minute doc check).
6. Tests: NO existing LABEL_LIST test anywhere in `test/` (grep-confirmed zero). New
   `test/sql/pushdown_filter_ir_containment.test` following
   `test/sql/pushdown_filter_ir_types.test` conventions; assert the predicate is pushed
   (prefilter), not rechecked, against a LABEL_LIST-indexed column.

### 6. Alternative route — un-gating `filter :=` (optional add-on, NOT a substitute)

- The `filter :=` named param is a raw string (`lance_search.cpp:188-195`) → namespace wire
  protocol only; guard `"filter parameter is only supported for namespace-backed tables"`.
- Guard is deliberate and load-bearing (`docs/sql.md:70,99,143`; `lance_search.cpp:558-560`
  skips WHERE-pushdown for namespace tables — `filter :=` is their ONLY prefilter; for local
  datasets WHERE pushdown already works, so `filter :=` would be a redundant second mechanism).
  Introduced with #217 (commit `4675388`).
- Un-gating locally is small (~2 files): `Scanner::filter(&str)` exists
  (`lance/src/dataset/scanner.rs:1240`) and the extension already uses the
  Expr→SQL-string→`scanner.filter` round-trip twice (`rust/ffi/dataset.rs:694-726`, `:858`).
- A string `array_has_any(tags, ['a','b'])` parses (DataFusion `SqlToRel`, native array-literal
  syntax) into the SAME Expr → same `create_filter_plan` → same LABEL_LIST recognition
  (`scanner.rs:448-462, 2445-2450`; `lance-datafusion/src/planner.rs:937`).
- Why it is NOT the fix: it only serves the explicit search functions with hand-written filter
  TEXT (value interpolation/escaping — violates the no-interpolation rule); it does NOT make
  plain `WHERE array_has_any(...)` push down; single-filter-slot interaction (AND vs replace
  with pushed WHERE IR) is untested territory.

### 7. Implementation checkboxes (verify while building, not design risks)

- Bound `$tags` parameter folds to a list constant through the new
  `TryGetNonNullListConstant` (scalar analogue already validated).
- Exact `datafusion-functions-nested` accessor names (`array_has_any` vs `array_has_any_udf`).
- Pushdown observability for the test: `explain_verbose` on `lance_vector_search`, and the
  k-semantics probe (`prefilter := true` returns k matching rows).

## Implications

- Kontext: **tags stay tags** — no promoted org-dimension columns, no generic slots, no lane
  string; the multi-tenancy closure's layering (org dims = app vocabulary) becomes physically
  viable. The oversample rule retires for containment; `k` drops from row-count to `n`.
- The dormant `tags_idx` wakes over existing data with zero migration (created eagerly for
  exactly this day — `KontextSchema.cs:30-38`).
- Bonus beneficiary: `array_has(cited_memory_ids, $id)` — "which memories cite X" lineage
  queries the BTREE indexes do not serve.
- Ship path: build the patched extension ourselves while the PR goes upstream. How the lance
  extension is bundled into KurrentDB is the one unverified logistics item.
