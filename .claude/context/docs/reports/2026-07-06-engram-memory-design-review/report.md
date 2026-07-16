---
title: Engram Memory Design — Review
type: review
date: 2026-07-06
author: sergio
tags: [engram, kontext, memory, event-sourcing, design-review]
scope: /Users/sergio/Downloads/engram-memory-design.html (Engram design doc, @0–@8)
related: [2026-07-03-memory-model-v2, 2026-07-03-agentic-memory-systems]
---

## Summary

Engram is a re-conception of the Kontext memory system: an event-sourced memory for
Claude built on KurrentDB (truth, append-only streams) + DuckDB (disposable projections,
FTS, vectors), exposed as JSON/HTTP over .NET 11 minimal APIs behind a single
`EngramService` facade. The central pivot from memory-model-v2 is dropping
(subject, predicate, value) triples with canonical keys in favour of **natural-language
claims whose identity is decided by meaning** (an LLM judge rules equivalent / supersedes /
contradicts / unrelated, and every verdict is itself an event).

**Verdict: a clear step up from v2 — coherent, well-argued, and the event model is clean.
The identity-by-meaning bet is worth making. It is buildable as written for Stage 1 once
four load-bearing issues (confidence formula, write-path latency, principal boundary, PII
redaction) have answers.**

The one design decision that de-risks the whole bet: **verdicts are events**, so the fold
is deterministic on replay even though the original judgment is not. That is the quiet
masterstroke and it holds the rest of the design together.

## Findings

### The central move, and the risk it carries

Dropping triples for natural-language claims judged by meaning (decisions L-1/L-2) is the
correct call. The justification is airtight: every writer and judge in this system *is* a
language model, so language is the native representation, not a compromise.

The trade to manage (not fix): v2's matching was brittle-but-deterministic; Engram's is
flexible-but-fallible, and that fallible thing now sits **on the identity mechanism itself**.
The doc states "a shared memory corrupted by guessed semantics is worse than no memory" and
then bets identity on guessed semantics. This is the risk that drives findings #2, #3 and
the Q-3 recommendation below. It is bounded — but only because verdicts are evented (see
Summary), which makes replay deterministic.

### Load-bearing concerns (ranked)

**1 — The confidence formula is underspecified and probably wrong.**
`confidence_at_read = source × decay × corroboration × retrievalScore`, all claimed to be
in [0,1] (@2 decay module; view aggregates lines 956–966; worked example line 1260).
- *Corroboration can't climb.* The worked example shows `corroboration: 1.0` at count 1. If
  a single claim is already 1.0, "more agreement → more confidence" is impossible inside
  [0,1] — there is nowhere up to go. Either a single claim must start below 1.0 and climb,
  or the corroboration factor is not bounded at 1. The count→factor mapping must be defined.
- *retrievalScore does not belong in confidence.* A fact is equally true regardless of how
  the query was phrased. Multiplying retrieval relevance into "overall confidence" makes the
  same fact report different confidence per query — an altitude confusion between "how well
  this matched" and "how sure we are." Keep both in `ConfidenceBreakdown` (already done) but
  do not fold retrieval into the epistemic number the agent trusts.

**2 — "The API never blocks" conflates two claims, and one is false.**
W-1 and line 738 use "never blocks" to mean *never blocks on concurrency* (ten writers →
ten `ClaimMade` events, nobody touches another's events) — true and good. But the
`ConflictChecker` runs "candidate lookup + verdict on make-claim" inline (component table
line 747; endpoint table line 982), so every `remember` **does** block on an LLM verdict's
latency. Decide explicitly whether the write-path conflict check is:
- (a) embedding-only nearest-neighbour — cheap, synchronous, no LLM — with the real verdict
  deferred to the reflector; or
- (b) a real small-model judge inline.
The doc currently reads as (b), which puts model latency on the hot path of the most common
operation.

**3 — Recall's principal boundary is unspecified, and it is a leak.**
R-3 makes recall cross-project, cross-session, cross-time by default but never states
cross-*principal* or not. `RecallQuery` carries `principalId` (line 994) but no isolation
rule. In a shared hive holding one principal's address, property transaction, and pet
medical data, "cross-everything" recall that ignores principal is a privacy breach, not a
feature. Wake-up is correctly scoped to `(principal, project)`; **recall should be
per-principal by default, cross-project** — state it in R-3 and enforce it in `RecallQuery`.

**4 — Redaction cannot be an afterthought for PII.**
S-4 is honest that tombstones don't work on shared streams. But the Stage-1 answer (purge
projections; the stream keeps the raw statement forever) means real personal data sits
recoverable in `memory-claims` until Stage-2 crypto-shredding exists. Projection purge is
not "right to be forgotten." Given the example payloads are literally someone's address,
property sale, and cat's medication: **either encrypt statement payloads from day one, or do
not put real PII in the MVP.** Do not ship option (b) from finding #2 with unencrypted PII.

**5 — `facts` is a view guarded by an unenforced invariant.**
`max(statement) FILTER (WHERE is_representative)` (lines 956–966) is correct only if exactly
one active claim per cluster is representative. If a fold bug ever yields zero or two, the
view returns NULL or a lexicographic-max statement — **silently wrong, not loud.** Add a
fold-time assertion or a `HAVING count(*) FILTER (is_representative) = 1` health check; a bug
here corrupts what "I know" with no error.

### Divergences from previously recorded decisions

These should get an explicit "yes, on purpose" before building.

- **Global claims stream vs per-workspace.** The recorded direction (memory-model-v2 work)
  was *single-stream-per-workspace* — the fix for unbounded per-topic streams. S-1 goes
  further to **one global `memory-claims` stream** with principal/project as event fields.
  Defensible (S-1 argues it well) but a step past the ruling, and it interacts directly with
  findings #3 and #4 (everyone's claims now share one stream). Confirm this supersedes the
  per-workspace decision.
- **HTTP/JSON-first vs protobuf-first.** Prior rounds were deep in protobuf contracts
  (Memory API v2 proto, transport-neutral ingestion proto). Engram commits to "JSON over
  HTTP on .NET 11 minimal APIs" with `EngramService` as the transport-neutral core and
  gRPC/MCP as later adapters. The facade-first framing is arguably *better* than proto-first,
  but it is a real reversal of recorded work — name it as intentional.
- **Consistent with prior decisions:** provenance as `(sessionId, logPosition)` (S-2)
  matches the "log position, not TFPos" ruling exactly.

### House-style nit

`MakeClaim.Confidence` is nullable with `Defaults.For(raisedBy.Kind)` (line 1016) — exactly
the "silent fallback default on a config value" the project conventions push against, and it
undercuts the claim-writing prompt that treats confidence as mandatory reasoning. Make it
required at the wire boundary, or accept that the prompt's discipline is only advisory.

### Framing point — the unboundedness moved, it didn't disappear

The root cause being fought — unbounded per-topic streams — relocated rather than vanished:
`memory-claims`, `memory-recall`, and the per-session streams are all unbounded by *use/time*
now instead of by *topic cardinality*. This is very likely the better failure mode (KurrentDB
scales streams-by-length far better than streams-by-count), but it is worth stating so nobody
believes the problem was eliminated rather than moved.

## Recommendations

Priority order for a pre-Stage-1 punch-list:

1. **Redefine the confidence model** (finding #1). Specify the corroboration count→factor
   mapping; remove `retrievalScore` from the epistemic "overall".
2. **Decide the write-path conflict-check story** (finding #2): embedding-only sync + deferred
   LLM verdict (recommended), or inline small-model judge with an accepted latency budget.
3. **Add the principal boundary to recall** (finding #3): per-principal default, cross-project.
4. **Resolve PII redaction for the MVP** (finding #4): encrypt statement payloads from day one
   if real personal data will be stored.
5. **Guard the `facts` invariant** (finding #5): assert exactly one representative per cluster.
6. **Ratify the two divergences** (global stream; HTTP-first) as intentional supersessions.

### Calls on the doc's open questions

- **Q-1** (representative statement): agree — consolidated when it exists, newest-active until
  then. Sub-question: fact-level validity = union of member ranges, but do not let
  consolidation *invent* a range no single claim asserted; the representative keeps its own.
- **Q-2** (entity kind enum vs free text): free text + rich description. The judge reads the
  description; an enum buys nothing and costs a migration every time reality grows a new kind.
- **Q-3** (sync judge auto-corroborate): **contradiction-only first.** Asymmetry matters —
  inline contradiction detection surfaces a *question* (safe, resolved by asking the human),
  but inline auto-corroboration silently *fuses two distinct facts* and every reader sees the
  merge until repair. When identity rests on a fast model's judgment, detect inline,
  corroborate in the reflector.
- **Q-4** (stage boundaries): agree with the author's own answer over the doc body —
  extraction belongs in Stage 2 *with* wake-up (they are each other's payoff: wake-up has
  nothing to show until extraction produces claim volume without manual effort). Reflection
  stands alone in Stage 3.

## Method

- **Source reviewed:** `/Users/sergio/Downloads/engram-memory-design.html`, all sections
  @0 (Thesis) through @8 (Simulation), read in full.
- **Cross-referenced against:** the session-memory record of the prior Kontext memory
  redesign rounds (per-workspace stream direction, protobuf/gRPC-first contracts), and the
  auto-memory rulings ("log position, not TFPos"; "DuckDB vector analysis is not a decision").
- **Approach:** design critique focused on load-bearing correctness, privacy, and latency
  risks, plus divergences from recorded decisions — not a line-by-line spec conformance pass.
- **Not covered:** implementation feasibility of the DuckDB HNSW/FTS retrievers at scale;
  eval methodology (F-2); the reflector's LLM prompt quality. These are deferred until the
  Stage-1 punch-list above is resolved.
