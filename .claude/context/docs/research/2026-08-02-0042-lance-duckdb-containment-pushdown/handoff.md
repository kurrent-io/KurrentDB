# Handoff — implement containment pushdown in lance-duckdb

> Companion to `research.md` in this folder — read that FIRST; it is the complete verified
> context (pipeline map, change surface, file:line references, checkboxes). This brief is the
> execution contract.

## Mission

Implement filter pushdown for **`array_has_any` and `array_has_all`** (operator-scoped: the
singular `array_has` is out of scope, though Lance recognizes it identically) in the
`lance-duckdb` DuckDB extension at `~/dev/contrib/lance-duckdb`, via the **IR route** —
change surface §5 of `research.md`.

Do NOT implement the `filter :=` un-gate (research §6) — documented alternative, explicitly
not part of this task.

## Ground rules

- Work on branch `feat/containment-pushdown` off current `main` (`63c2446` or later).
  **Never commit to `main`.** No push, no PR without the operator's explicit word — upstream
  fork mechanics are decided at review time.
- Match the repo's own conventions (its formatting, test style, naming). The pattern to copy
  exists twice: the `starts_with`/`contains` branches in `src/lance_filter_ir.cpp`.
- Discover the build/test commands from the repo itself (README/Makefile/CI workflows) —
  they were not verified in the research pass. Record what you find.

## Acceptance criteria

1. `SELECT … FROM lance_hybrid_search(...) WHERE array_has_any(tags, ['a','b'])` and the
   `array_has_all` variant against a **local** dataset with a `LABEL_LIST` index:
   - predicate is **pushed down** (prefilter), not post-checked by DuckDB;
   - with `prefilter := true`, returns k MATCHING rows (the k-semantics probe);
   - verify pushdown observably (e.g. `explain_verbose` on `lance_vector_search`, or the
     repo's own pushdown-assertion test idiom).
2. Same predicates push down on plain scans (`SELECT … FROM 'x.lance' WHERE array_has_any(…)`).
3. **Bound parameters work**: the list argument arrives as a bound value (not interpolated) and
   still folds to a list constant (checkbox §7 of research.md).
4. New test `test/sql/pushdown_filter_ir_containment.test` (follow
   `pushdown_filter_ir_types.test` conventions) covering: both functions, VARCHAR[] elements,
   at least one non-string element type, cast-wrapped constants, and the negative case
   (non-constant second argument → falls back gracefully, no crash, correct results).
5. Entire existing test suite stays green.

## Known checkboxes (from research §7)

- Exact `datafusion-functions-nested` accessor names — 2-minute doc check.
- Promote `datafusion-functions-nested` from transitive to explicit in `Cargo.toml`.
- LIST literal wire format is the one genuinely new design piece (both encode and decode
  sides) — keep it recursive over the existing scalar literal tags.

## Report back

Branch name, commits, build/test commands discovered, acceptance evidence (test output +
pushdown proof), and any deviation from the researched change surface with the reason.

---

## Operator initiation prompt (paste into a fresh session started in ~/dev/contrib/lance-duckdb)

```
Read these two documents completely before touching anything — they are your entire context:

1. /Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/research/2026-08-02-0042-lance-duckdb-containment-pushdown/research.md
2. /Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/research/2026-08-02-0042-lance-duckdb-containment-pushdown/handoff.md

Mission: implement array_has_any + array_has_all filter pushdown in this repo
(lance-duckdb) via the IR route, exactly per the change surface in research.md §5 and the
acceptance criteria in handoff.md. Work on branch feat/containment-pushdown; never touch
main; no push or PR without my explicit word. Discover build/test commands from the repo
itself. When done, report per the "Report back" section of handoff.md.
```
