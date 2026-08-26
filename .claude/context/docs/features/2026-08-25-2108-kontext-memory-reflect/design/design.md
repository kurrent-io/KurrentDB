---
title: Kontext Memory Reflect
status: withdrawn
authors: [sergio]
date: 2026-08-25
tags: [kontext, memory, reflection, llm, generative-agents, withdrawn]
---

# Design Space — Kontext Memory Reflect

<!--
Working doc. Brainstorm, discussion, and decisions for this feature. Deliberately informal and
append-leaning — you add to it, you mark decisions, you do not rewrite the history of the discussion.
Kept for the life of the feature. Once it settles, distill the outcome into prd/prd.md and spec/spec.md,
and slice releases into plans/. This doc is also the feature's decision record — keep the rejected
options; the "why not" is the value. Sources this design space cites go in design/refs/.
-->

> `reflect` shipped as a contract and an MCP tool that throws `NotImplementedException`. This records
> what the operation was for and how it would be built, so removing it costs no thinking. Written
> immediately before the removal.

## Problem / Trigger

`memory.proto` carried `rpc Reflect`, `McpMemoryService` registered a `reflect` tool, and
`McpInstructions.resx` gave agents eighteen lines telling them when to reach for it. The
implementation was:

```csharp
throw new NotImplementedException(
    "Reflect synthesizes derived memories with a language model — not part of the data-store surface.");
```

An agent reads the instructions, calls the tool, and gets an exception. That is worse than the tool
being absent — a missing capability costs one recall the agent does itself, while a throwing one costs
a failed turn and teaches the agent nothing about what to do instead.

The trigger was noticing this while auditing the memory surface, and asking whether to implement it or
remove it.

## What reflect IS

The act of reflect is **concluding**, not summarizing. That distinction drives every design choice
below.

**The discriminating test:** if the output could be found by reading any one of the inputs, it is a
summary and the reflection failed. A reflection asserts something *entailed by* the inputs and
*present in none of them* — a claim that could not have been recalled before, because it did not exist
until the pile was read together.

Three neighbouring operations it is not:

| | Does what | Who owns it |
|---|---|---|
| Summarize | compresses N memories into shorter text — loses information, adds none | nobody; not wanted |
| Deduplicate | folds two memories that say the same thing | the curation pass. `retain` explicitly refuses to |
| **Reflect** | **derives a claim none of the inputs makes** | the model |

The origin is Park et al., *Generative Agents* (2023), where reflection is what stops an agent
drowning in observations. See `[[kontext-ground-in-generative-agents]]` in agent memory.

## Exploration

### The shape, if it were built

The move that matters, and the one that gets skipped:

```
recalled memories
      │
      ▼
  "what questions can we now answer that no single one of these answers?"
      │                                       ← THIS is the reflection
      ▼
  for each question: claims + the memories each rests on
      │
      ▼
  new memories, citing downward
```

Going straight from memories to insights makes the model summarize, every time. Asking *"what do these
mean together"* produces prose. Asking *"what can we now conclude that none of these says"* produces a
claim.

### Output shape

Kontext's write rule ("store the claim you checked; name what would show you wrong") dictates
structured proposals rather than text:

| Field | Content |
|---|---|
| `content` | the claim, standing on its own — readable months later with no other context |
| `memory_type` | `FACT` or `PREFERENCE`. Never `OPEN_QUESTION`: a question is asked, not concluded |
| `evidence` | `MemoryRef`s to the memories it rests on — what makes it auditable |
| `reasoning` | why those inputs entail this claim, so a reader can discount it |
| `supersedes` | the ones this makes wrong or redundant |

`reasoning` is load-bearing. A reflection is an *argument*, and the argument is the part a reader needs
in order to disagree. Without it you have an assertion from a model with no way to check the inference.

### Reflect needs no new write rules

The proposals fold into a `RetainRequest` and go through `RetainAsync`, which already enforces
supersede-a-live-tip, citation existence, and all-or-nothing rejection. This is the strongest argument
that the operation is tractable at all — the dangerous parts are already guarded.

### Most reflections should supersede NOTHING

Three memories —

> the build failed on X · the build failed on Y · the build failed on Z

reflecting to *"the build fails whenever the analyzer runs incrementally"* must **not** supersede them.
They are the evidence. Delete the evidence to keep the conclusion and the store keeps a claim nobody
can check, derived from things that can no longer be read.

Supersession is for when the reflection makes a specific prior claim *wrong or strictly contained* —
"we use DuckDB for the index" superseded by "we use DuckDB for the index and Lance for vectors". Same
claim, corrected. That is a fold, not a derivation.

The model's instinct is to tidy up. Any prompt would have to state this constraint harder than
anything else in it.

### Clustering by score is unsound

`RelatedFloorAndPipelineProbeTests` measured the worst true duplicate at `0.5000` and the best
non-duplicate at `0.5000` — see
`.claude/context/docs/research/2026-08-21-0017-lance-hybrid-search-semantics/`. No similarity threshold
separates them, so grouping candidates by score cannot work. The model must do the grouping, over a
recalled set small enough for one context window.

### Reflections are memories

`evidence` is a `MemoryRef`, so a reflection can cite a reflection — Park's tree falls out for free,
abstraction stacking on abstraction with every level traceable down to observations. It also means a
wrong reflection propagates, which is what the cascade job exists for.

### Trigger

Park fires reflection automatically once accumulated importance crosses a threshold. The shipped proto
made it agent-invoked — "reach for it periodically, when a pile of memories wants to become a durable
conclusion". The automatic version is the obvious later move: a background pass keyed on importance
accumulated since the last one, which also removes the dependency on an agent remembering to do it.

## Decisions

- **2026-06-12 — Reflection is agent-side, not server-side.** Recorded in
  `.claude/context/docs/designs/2026-06-12-memory-model-v2/design.md:342-349`: *"Kontext's promise is
  no LLM in the box — and every caller IS an LLM: the shipped skill + tool descriptions instruct agents
  to periodically recall → synthesize → retain with provenance. Zero server machinery; the contracts
  already carry it."* Server-side reflection is listed under **Out of scope (deliberately)**.

- **2026-08-25 — Remove `reflect` entirely.** The shipped `rpc Reflect` contradicted the 2026-06-12
  decision: it is server-side reflection, deferred on purpose. Removing it restores the recorded design
  rather than merely deferring an unimplemented tool. Rejected alternatives:
  - *Keep the contract, remove only the MCP tool* — leaves `ReflectRequest`, `ReflectResponse` and
    `ReflectionCompleted` referenced by no rpc. Just as confusing, only quieter.
  - *Implement it now* — blocked on three things, below. Shipping autonomous supersession with no way
    to judge its output is worse than not having it.

- **2026-08-25 — Agents keep doing reflection themselves.** `recall` → synthesize → `retain` with
  `evidence` and `supersedes` already expresses the whole operation, and the contracts carry the
  provenance. Nothing is lost to a caller that is itself a language model.

## Blockers, if it is revived

In order of size:

1. **No chat client is wired.** `IChatClient` is consumed by `ClaimMatcher`
   (`Modules/Memory/Matchers/`) and `ChatRelevanceModel` (`Kurrent.Kontext.Retrieval`), but registered
   nowhere in `KontextWireUp` or `KontextMemoryWireUp`. Reflect commits a running node to an LLM
   dependency on a write path — provider config, credentials, latency, cost, failure handling. That is
   the real decision, not the reflect method. Wire a chat client for a reason that already exists,
   then reconsider reflect.
2. **The async contract was never decided.** The proto said long-running, completing via
   `ReflectionCompleted`; the signature returned a response synchronously. Either the rpc becomes
   fire-and-forget returning a `query_id`, or it blocks for as long as the model takes.
3. **Nothing can judge the output.** Every other memory operation has a checkable outcome — retain
   stored or NOOPed, recall ranked, reclaim returned. A reflection's output is a *claim*, and the only
   test is whether it turns out true and useful later. Reflect is also the sole operation that
   supersedes autonomously, rewriting the memory graph on a model's judgment, silently. An eval corpus
   comes first; `Kurrent.Kontext.Tests/Integration/Corpus` and `longmemeval_oracle.json` are candidates.

## Open Questions

- Does the automatic, importance-triggered variant belong in Kontext at all, or is a scheduled agent
  pass the honest home for it given the no-LLM-in-the-box promise?
- If a chat client is eventually wired for another reason, does reflect become obviously cheap, or do
  blockers 2 and 3 still dominate?
- Is there a useful non-LLM reflection — purely structural derivations over tags, supersession chains,
  or temporal clustering — that would carry some of the value with none of the judgment risk?
