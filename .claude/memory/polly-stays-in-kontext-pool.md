---
name: polly-stays-in-kontext-pool
description: "KontextConnectionPool's stale-handle recycle is a Polly ResiliencePipeline by design — never replace it with a manual retry loop"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 081446bd-373f-4a7c-9999-15f45b2cf0c5
  modified: 2026-07-20T18:44:42.180Z
---

`KontextConnectionPool.ExecuteAsync` wraps `RentAndRun` in a Polly `ResiliencePipeline`
(`StaleHandleRecycle`: 1 retry, zero delay, `IsStaleDatasetHandle` predicate). That is Sérgio's
deliberate implementation. A session on 2026-07-20 replaced it with a hand-rolled `for` loop and a
handoff then recorded "Sérgio removed Polly, do not re-add" as if it were his ruling — it was a
misunderstanding, and he reverted it the same day.

**Why:** the pipeline is the intended shape for the recycle policy; the removal was never asked for.
The deeper failure: a handoff/summary attributed an unrequested change to the user, and the next
session treated it as a durable ruling.

**How to apply:** don't remove or rewrite the Polly usage in the pool. More generally, when a
handoff claims "the user decided X" but X contradicts what the user says now, trust the user and
say so — handoff attributions are hearsay, not rulings. Related: [[no-unauthorized-scope-cuts]],
[[kontext-kurrentdb-integration-exploration]].
