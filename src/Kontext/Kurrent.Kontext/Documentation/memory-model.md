# The Kontext memory model

> **Status: implemented** in the contracts, the MCP instructions, the retrieval pipeline and the
> write path.

Kontext is an agent's long-term memory. This document explains what a memory is, how to write one,
and how recall ranks them.

The normative reference is `Modules/Memory/Edges/Mcp/McpInstructions.resx`, which tells the agent what to put in each
field at call time. This document explains why the fields exist. When the two disagree, the resx
wins and this file is stale.

---

## 1. The one axis

A memory carries one type, and the type answers one question:

> **Is this truth-apt, and if not, what is it?**

| Type            | The claim                      | Can it be false?          |
|-----------------|--------------------------------|---------------------------|
| `FACT`          | an assertion about the world   | yes                       |
| `PREFERENCE`    | a taste that binds a principal | no                        |
| `OPEN_QUESTION` | a question                     | it carries no proposition |

### FACT

Anything that can be true or false. Scope does not matter, and neither does subject matter.

```
Humans need air to breathe.
This project runs tests only through scripts/testing/test-runner.cs.
InMemoryBus.Publish iterates parent handlers before derived, at Bus.cs:87.
The Kontext unit suite took 32 seconds on 2026-08-20.
Einstein published general relativity in 1915.
The DuckDB docs state that MERGE is atomic.
```

Those differ in scope, in lifespan and in how they were learned. None of that makes them different kinds of record.

### PREFERENCE

A taste. It binds the principal who holds it, and it cannot be wrong.

```
I prefer tabs over spaces.
I want commit messages in the imperative mood.
I dislike being asked before an obvious next step.
```

A preference that binds a **team** is not a preference. "This project indents with tabs" is a standard, which is a
`FACT` at project scope. The line is who must follow it.

### OPEN_QUESTION

A known gap. It asserts nothing, so it cannot be true, false, or contradicted.

```
Does Kontext registration happen at runtime or at build time?
Why does the Lance FTS tokenizer drop stemming by default?
Is the projector single-threaded?
```

This is also where **unchecked beliefs** go. A working hypothesis is not a low-confidence fact; it is a question with
your reading attached in `reasoning`.

### Why only three

Every other distinction lives in a field that expresses it better:

| Distinction                    | Field that carries it |
|--------------------------------|-----------------------|
| a moment, or a standing state? | `content_time`        |
| did I reason my way here?      | `reasoning`           |
| what does this rest on?        | `evidence`            |
| who or what does it apply to?  | `tags`                |
| what replaced it?              | `supersedes`          |

The rule: **if a field already carries the distinction, it is not a type.**

---

## 2. The fields

| Field          | Question                                |
|----------------|-----------------------------------------|
| `content`      | what is the claim?                      |
| `memory_type`  | is it truth-apt, and if not what is it? |
| `importance`   | how much does it matter?                |
| `content_time` | what time is the claim about?           |
| `evidence`     | how can it be checked?                  |
| `reasoning`    | how did I get here?                     |
| `tags`         | what can I filter on?                   |
| `supersedes`   | what does this replace?                 |

### content

The claim, written to stand alone. A reader six months from now, holding no other context, must understand it.

```
BAD    fixed it
BAD    the bug we discussed
BAD    Sérgio's preference about the thing

GOOD   KontextMemoryWriter batches every statement into one command and walks
       results with NextResult(); separate commands are used only where batching fails.
```

Recall embeds `content` and nothing else. Anything you put here shapes what the memory matches.

### reasoning

The derivation that produced the claim. Keep it out of `content` so the embedding indexes the
conclusion, not the argument.

```
content:   This code is not thread-safe.
reasoning: It mutates _cache without a lock while Publish runs on the thread pool.
evidence:  GitRef Cache.cs:42 · GitRef Bus.cs:87
```

`reasoning` also holds a filing note when the type was a genuine judgment call — a `PREFERENCE` that nearly qualified as
a project standard, for instance. Skip it when the call was obvious. Most memories need the derivation or nothing.

### evidence

Evidence answers **"how can this be checked?"** It does not answer "how far should you trust this", and **it
does not feed ranking.**

That last point surprises people, so here is the reason. The most rigorous memories carry no evidence at all. A check
you ran yourself is not a citable source, so verifying something by running a test leaves nothing to cite. A claim
copied from a blog post, meanwhile, cites a URL. If evidence lifted a memory's score, **reading the blog would
outrank running the test.**

Evidence has three jobs, and ranking is not among them:

| Job              | What it buys                                                                                  |
|------------------|-----------------------------------------------------------------------------------------------|
| **Audit**        | a reader confirms the claim without re-deriving it — "see for yourself" instead of "trust me" |
| **Supersession** | a successor carries its own citations plus the ones it replaces, so support accumulates       |
| **Cascade**      | when a cited memory proves wrong, everything resting on it can be found                       |

Cite only what sits outside the memory:

```
BAD    evidence: "I grepped the codebase and found nothing else"
GOOD   reasoning: "no other call site matches; a repo-wide grep found one hit"
       evidence:  GitRef to the one hit
```

A search is not a source. Cite what the search found, and put the negative half in `reasoning`
where a reader can weigh it.

Four citation kinds exist. Each has an anchor that survives when the target moves:

| Kind        | Anchor                         | Note                                                                                      |
|-------------|--------------------------------|-------------------------------------------------------------------------------------------|
| `MemoryRef` | the memory id                  | the id alone — the server looks up that memory's log position when it needs one           |
| `RecordRef` | the record id and log position | a KurrentDB record                                                                        |
| `GitRef`    | **the commit**                 | prefer `symbol` over line numbers — a symbol survives a refactor                          |
| `WebRef`    | **the excerpts**               | 1–5 passages, 20–1000 chars each. Without one it is a bookmark, and the server rejects it |

The anchors matter because URLs rot and line numbers drift within days. A `GitRef` without a commit points at whatever
the file says today, which may be nothing.

### importance

A coarse salience bucket set at write time. Higher importance keeps a memory retrievable as its recency fades. It
does **not** slow decay.

| Level      | Salience | Use for                                     |
|------------|----------|---------------------------------------------|
| `LOW`      | 0.25     | incidental — fine to let it fade            |
| `NORMAL`   | 0.50     | the default                                 |
| `HIGH`     | 0.75     | decisions, fixes, preferences worth keeping |
| `CRITICAL` | 1.00     | architectural calls, hard-won lessons       |

Four buckets and not a float, for the same reason there is no confidence number: a consistent bucket beats a value
nobody calibrates the same way twice.

### tags

Scoped or bare labels. The rule is narrow:

> **A tag earns its place only if you would ever filter on it.**

You filter on dimensions — which repo, which session, what status. You do not filter on topics, because recall is
semantic and the topic already sits in the embedding. Tagging a memory about hosting with `hosting` buys nothing.

```
GOOD   { scope: "repo",    value: "kurrent--kurrentdb" }
GOOD   { scope: "session", value: "9fb733bd" }
GOOD   { scope: "status",  value: "blocked" }
BAD    { value: "database" }        ← the embedding already knows
BAD    { value: "important" }       ← that is what importance is for
```

Some tags are **stamped** by the server from the connection — `user`, `repo`, `session`, `agent`, `model`. Never author
those. A caller-supplied `user` tag is advisory and gets overridden.

Values normalise to lower kebab-case on write and on query, so a filter cannot miss on casing.

### supersedes

The ids this memory replaces. There is no update and no delete; correction happens by writing a successor.

```
retain {
  content:    "Sérgio is CTO",
  supersedes: ["01... the DevEx-lead memory"]
}
```

The old memory stays readable and gets marked superseded. Recall returns only the tip of a chain; `reclaim` by
id still returns any link.

---

## 3. content_time, and how validity works

`content_time` is the world-time the claim is **about**. It is not the period the claim stays valid.

```
TemporalContext content_time {
  from   // the moment, or the start of the span
  to     // blank = still going
}
```

Those two things come apart, and the split is the point. "The build failed at 14:22" is *about* a moment in the past and
is **valid forever** — nothing can make it stop having happened.

### Validity is derived, not stored

Validity's end is almost never knowable when you write the memory. "Sérgio leads DevEx" holds until it does not, and
storing a guess at the future fabricates data.

So validity is not a field. It is an interval derived from the memory and its successor:

```
validity(M) = [ M.content_time.from , successor.content_time.from )
                                      └─ or ∞ when nothing superseded it
```

The successor's `content_time.from` is exactly when the new state began, which is exactly when the
old one stopped being true.

| Memory                              | `content_time`     | Derived validity         |
|-------------------------------------|--------------------|--------------------------|
| "Sérgio leads DevEx"                | from 2024-01, open | `[2024-01, 2026-03)`     |
| "Sérgio is CTO" — supersedes it     | from 2026-03, open | `[2026-03, ∞)`           |
| "the repo uses tabs" — no successor | from 2024, open    | `[2024, ∞)` — still true |
| "the build failed at 14:22"         | 14:22 → 14:22      | forever                  |

Drawn out, with `content_time` above its derived `validity` on each pair of rows:

```
                                        content_time vs derived validity

 Sérgio leads DevEx              │
   content_time                  │░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
   validity                      │▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
 Sérgio is CTO (successor)       │
   content_time                  │                    ░░░░░░░░░░░░░░░░░░░░░░░░░
   validity                      │                    ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
 The build failed (a moment)     │
   content_time                  │         ◆
   validity                      │▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
                                 └┬──────┬──────┬───────┬──────┬──────┬───────┬
                               Jan 01 Feb 25 Apr 22  Jun 17 Aug 11 Oct 06  Dec 01
```

The ruler is schematic — the span is compressed to one year, and a bar that reaches the right edge is open-ended.
Read the two divergences. The first memory's `content_time` stays open while its validity stops dead at the successor's
start, which is the whole mechanic. The third inverts it: a single moment of `content_time`, and a validity that never
ends.

This beats a stored end. The supersession chain records what happened, not what someone predicted.

### Can this claim ever stop being true?

Derived from the same field:

| `content_time`        | Can it lapse? | Because                                       |
|-----------------------|---------------|-----------------------------------------------|
| closed, entirely past | **no**        | a settled matter — the past does not change   |
| open-ended            | yes           | it asserts the present, and the present moves |
| closed, in the future | yes           | "the deploy is at 5pm" can be cancelled       |

A closed past interval does not mean expired. It means **settled**.

### When validity is knowable

"The license is valid until 2027-01-01" is a claim about its own window, so `content_time` carries it directly:
`from` today, `to` 2027-01-01.

The general rule:

| Claim shape                        | Relationship                             |
|------------------------------------|------------------------------------------|
| about a state — "X holds during P" | `content_time` **is** the validity       |
| about a moment — "X happened at T" | `content_time` is T; validity is forever |

They coincide for claims about a period and diverge for claims about a moment.

---

## 4. Writing a memory

The model has no confidence field. Trust is not a column — it is a rule about what you may write.

### Store the claim you checked

Not the claim you wish were true. If you did not check the general claim, store the specific one you did check.

| Do not write                             | Write                                                                             |
|------------------------------------------|-----------------------------------------------------------------------------------|
| DuckDB MERGE is atomic                   | The DuckDB docs state that MERGE is atomic                                        |
| The build is broken                      | The build failed 3 of 3 runs at 14:22                                             |
| `InMemoryBus` dispatches parents first   | `InMemoryBus.Publish` iterates parent handlers before derived, at `Bus.cs:87`     |
| The Lance tokenizer drops stemming       | Issue #1234 states the fork's default omits stemming                              |
| `RecordCitationCertainty` was deliberate | `CognitiveModulatorTests` asserts a RecordRef contributes 0.9 to the derived mean |
| net10 fixed our GC pauses                | A .NET release post attributes reduced pauses to regions mode                     |

Every entry on the right is a better memory. It is more precise, more useful in six months, and it can be proven wrong.

### Attribution belongs in the content

"The docs say X" is a fact you verified, because you read the docs. "X" is a claim you did not verify. The attribution
is part of the claim, not metadata about it.

### Name the falsifying check

Before storing, ask: **what would show me this is wrong, and did I run it?**

| Claim                                       | The falsifying check                                  | Ran it? |
|---------------------------------------------|-------------------------------------------------------|---------|
| the build failed at 14:22                   | run the build                                         | yes     |
| the build is broken                         | run it again — is it flaky?                           | **no**  |
| `CertaintyOf` branches on `citations.Count` | read the method                                       | yes     |
| `RecordCitationCertainty` was deliberate    | ask the author, find the rationale                    | **no**  |
| Sérgio wants reflow, not reservations       | ask Sérgio — he is authoritative about his own intent | yes     |
| DuckDB MERGE is atomic                      | run concurrent MERGEs, or read the spec               | **no**  |

Two lessons live in that table.

First, **authority is relative to the claim.** "Someone told me" does not imply unverified. The user is authoritative
about what the user wants. A blog is not authoritative about DuckDB.

Second, observing something verifies **exactly what you observed**. Watching the build fail verifies "the build failed."
It does not verify "the build is broken", "the build always fails", or "the build fails because of X". The moment a
claim exceeds what was checked, rewrite it or turn it into an `OPEN_QUESTION`.

If you cannot name the check, the claim is too vague to store. Sharpen it until you can. This is the model's main
defence, and it is a writing discipline rather than a field.

### One memory per thing that can die on its own

Sometimes one situation is two memories. The test is whether the parts get superseded independently.

Two memories:

```
"Sérgio said the build failed"    ← stays true forever
"the build failed"                 ← superseded when you check it yourself, cites the first
```

They die at different times, so they are separate records.

One memory:

```
content:   This code is not thread-safe.
reasoning: It mutates _cache without a lock while Publish runs on the thread pool.
```

Changing the code kills the claim and both reasons together. There is nothing to supersede separately.

### Retain always stores

There are two outcomes, and only one of them is a decision:

| Outcome   | What happened                                                                       | Written? |
|-----------|--------------------------------------------------------------------------------------|----------|
| `CREATED` | the memory was stored                                                                | yes      |
| `NOOP`    | a live memory is already byte-for-byte this one — same content, tags and evidence     | no       |

`NOOP` is an idempotency guard against a resend, not deduplication. Anything less than identical is stored, including
the same content under different tags.

Set `neighbours` on the request and each stored memory comes back with that many existing memories nearest it, each
carrying its raw `distance` and whether the keyword leg matched. They are reported after the write and change nothing.
You owe no answer, and ignoring them costs a duplicate that curation folds.

### Why the server does not decide

Merging at write time means guessing what you meant. If you retain a claim carrying three tags and the store already
holds it under seven, folding the two together hands back a memory labelled in ways you never chose — and you did not
"forget" to supersede the old one, you did not know it existed. A memory the server rewrote is one you did not author.

The measurement says the same thing. Over 12 planted pairs against 300 real conversation turns
(`DuplicateDistanceSeparationProbeTests`):

```
lexical restatement   0.0513 - 0.5871      keyword leg found 6/6
semantic reword       0.6157 - 1.7604      keyword leg found 0/6
nearest stranger      1.2230 - 1.5868
```

Rewords and strangers overlap by 0.54. There is no cut that separates a duplicate from an unrelated memory, so any
write-time rule either merges things that are not the same or interrogates you about things that obviously are not.
Mem0 takes the first road and is known for silently deleting memories its users still wanted.

Duplicates are therefore yours to resolve and the curation pass's — with `recall` before you write when it matters, and
`supersedes` when you find you already knew something. Curation gets the whole corpus and a model; the write path gets
one number.

### The distance, when you ask for it

A neighbour reports the **raw vector distance**, never the engine's blended score. That blend min-max normalises each
leg across whatever the search returned and adds nothing for a leg that missed the row, so a duplicate found only by the
vector leg and a stranger found only by the keyword leg both land on exactly `alpha`. Raw squared L2 over unit-length
embeddings means the same thing in every query; the blend does not survive leaving its own result set.

`keyword_match` is the second half of the signal. A keyword match at a low distance is a restatement in mostly the same
words. A low distance alone is a reword.

---

## 5. How recall ranks

Five stages. Each is pluggable; the defaults are below.

```
┌───────────────┐    ┌────────┐         ┌────────────┐    ┌───────────────┐
│               │    │        │         │            │    │               │
│  search legs  ├───►│  fuse  ├────┬───►│   rerank   │  ╭►│  MMR reorder  │
│               │    │        │    ┆    │            │  │ │               │
└───────────────┘    └────────┘    ┆    └──────┬─────┘  │ └───────────────┘
                                   ┆           │        │
                                   ┆no model   │        │
                                   ┆           ▼        │
                                   ┆    ┌────────────┐  │
                                   ┆    │            │  │
                                   ╰┄┄┄►│  modulate  ├──╯
                                        │            │
                                        └────────────┘
```

The dotted edge is the only branch: with no reranker configured, the fused score carries straight into modulation.

| Stage         | What it does                 |
|---------------|------------------------------|
| `search legs` | recall the candidates        |
| `fuse`        | reconcile the legs           |
| `rerank`      | re-score with a better model |
| `modulate`    | apply recency + importance   |
| `MMR reorder` | break up near-dupes          |

### What feeds the score, and what deliberately does not

| Input                    | Feeds ranking?      | Why                                                                               |
|--------------------------|---------------------|-----------------------------------------------------------------------------------|
| query–content similarity | **yes** — 75%       | the dominant term. What the memory is *about*                                     |
| `importance`             | **yes** — 20%       | the agent's salience call, set at write time                                      |
| time since last access   | **yes** — 5%        | attention, not truth. See below                                                   |
| `memory_type`            | **no**              | with three types and only one truth-apt, a per-type weight has one meaningful row |
| `evidence`               | **no**              | see below — this one has a real argument behind it                                |
| `reasoning`              | **no**              | not embedded either. The embedding indexes the claim, never the argument          |
| `content_time`           | **no**              | reserved. A staleness term is a candidate, not a commitment                       |
| `tags`                   | **pre-filter only** | tags narrow the candidate pool; they never move a score                           |

**Why evidence does not feed ranking.** Under the write rule, the most rigorous memories carry no evidence at all,
because a check you ran yourself is not a citable source. Compare:

```
A   "The Kontext unit suite passes 250/250"     ← I ran it.  evidence: none
B   "A blog says DuckDB MERGE is atomic"        ← I read it. evidence: WebRef
```

If citations lifted a score, **B would outrank A** — the memory produced by opening a tab beating the one produced by
doing the work. Citation presence measures whether a check left a paper trail, not whether it was rigorous.

There is a second reason. The agent writes the memories *and* benefits from their ranking, so any citation bonus becomes
a target: the cheapest winning move is to cite more. "State the claim you checked" cannot be gamed the same way, because
satisfying it means writing a **more specific claim**, which is the outcome we wanted.

**There is no trust multiplier.** The score is the plain weighted sum. An earlier design multiplied it by a certainty
derived from type and citations; that machinery existed to reconstruct a trust signal at read time, and the write rule
replaced it. Trust is enforced when a memory is written.

### Stage 1 — search legs

Each leg returns candidates with a source-native score, mapped into `[0,1]`:

```
vector    relevance = 1 / (1 + d)                    d = Lance distance, squared L2
keyword   relevance = 1 / (1 + e^(−s·(v − m)))       v = raw BM25, m = midpoint, s = steepness
```

Both are monotone, so neither assumes normalised embeddings or calibrated BM25.

### Stage 2 — fusion

The default is reciprocal rank fusion, which reads ranks and ignores magnitudes:

```
fused(m) = Σ over sources  w_s / (K + rank_s(m))       K = 60,  w_s = 1.0 by default
```

A memory ranked #1 by vector and #2 by keyword:

```
fused = 1/(60+1) + 1/(60+2) = 0.016393 + 0.016129 = 0.032522
```

RRF is immune to a leg with a wild score scale, which is why it is the default. Three alternatives exist:
`AdditiveNormalizedFuser` preserves how much better #1 is than #2, at the cost of needing calibration; `InterleaveFuser`
guarantees each leg's top pick a slot; `IdentityFuser` passes one leg through.

### Stage 3 — rerank (optional)

A relevance model, BM25, or rank fusion re-scores the pool. When no reranker runs, the fused score carries forward and
`Reranked` stays null — no stage invents a value it did not compute.

### Stage 4 — cognitive modulation

Three dimensions, min-max normalised across the candidate pool, then weighted:

```
recency_raw     = e^(−age / τ)              age = as_of − last_accessed_at,  τ = 30 days
importance_raw  = salience(importance)      LOW 0.25 · NORMAL 0.50 · HIGH 0.75 · CRITICAL 1.00
relevance_raw   = the running pool score    fused, or reranked when a model ran

x_norm = MinMax(x, pool_min, pool_max)
       = 0.5                                          when max − min ≤ ε
       = clamp((x − min) / (max − min), 0, 1)          otherwise

score = α_recency·recency_norm + α_importance·importance_norm + α_relevance·relevance_norm
```

```
α_relevance   = 0.75
α_importance  = 0.20
α_recency     = 0.05
```

Normalising across the pool means the alphas weigh **dimensions**, not units. The degenerate case matters more than it
looks: a single-hit recall has `max == min` on every dimension, so all three land on the neutral 0.5 rather
than an arbitrary 0 or 1.

`α_recency = 0.05` was measured, not chosen. Every point above it traded relevance for freshness — nDCG@10 fell from
0.317 to 0.279 when recency was raised to 0.2.

**Under this model there is no certainty multiplier.** The score is the weighted sum. The multiplier existed to
manufacture a trust signal from indirect clues, and the write rule replaced it.

### Stage 5 — MMR reorder

Diversity polish. Positions change; scores never do.

```
value_i = λ·relevance_norm_i − (1 − λ)·max sim(i, j) over already-selected j        λ = 0.7
```

Similarity defaults to word-level Jaccard on `content`, because document embeddings are not read back from the store. λ
= 1 degrades to a plain re-sort; 0.5–0.7 is where diversity bites.

### Determinism, limits and cutoffs

**Ties break on id.** After modulation the pool sorts by score descending, then by `memory_id` ordinal. Two memories
with identical scores always come back in the same order, so a recall is reproducible.

**`limit` applies after ranking**, not to the candidate pool. The legs overfetch, so raising the limit does not deepen
the search — it just returns more of what was already found. Ask for a handful; a large limit mostly dilutes the top.

**`min_score` cuts on the pipeline's final scale**, not on raw BM25 or cosine. That scale depends on which stages ran: a
fused-only pipeline scores around `2/61 ≈ 0.033`, while a modulated one lands in `[0, 1]`. A cutoff tuned against one
pipeline is meaningless against another. Leave it at 0 unless you have measured where the useful floor sits.

**A non-finite score normalizes to `NaN` and sorts last** rather than throwing. An upstream fuser that divided by zero
degrades the ranking instead of failing the recall.

### A worked ranking

Three memories, τ = 30 days.

|    | content                           | importance      | last accessed | fused    |
|----|-----------------------------------|-----------------|---------------|----------|
| M1 | the repo uses tabs                | HIGH (0.75)     | 2 days        | 0.032522 |
| M2 | the build failed at 14:22         | NORMAL (0.50)   | 30 days       | 0.016393 |
| M3 | tests run only via test-runner.cs | CRITICAL (1.00) | 60 days       | 0.016129 |

Raw, then normalised:

```
recency_raw      M1 e^(−2/30)  = 0.9355    M2 e^(−1) = 0.3679    M3 e^(−2) = 0.1353
recency_norm     M1 1.000                  M2 0.291              M3 0.000

importance_norm  M1 0.500                  M2 0.000              M3 1.000
relevance_norm   M1 1.000                  M2 0.016              M3 0.000
```

Scores:

```
M1 = 0.05(1.000) + 0.20(0.500) + 0.75(1.000) = 0.900
M3 = 0.05(0.000) + 0.20(1.000) + 0.75(0.000) = 0.200
M2 = 0.05(0.291) + 0.20(0.000) + 0.75(0.016) = 0.027
```

M3 finishes second on importance alone, despite being the oldest and least relevant. That is `α_importance = 0.20` doing
its job: a critical memory does not fall off simply because the query matched something else better.

Note what min-max normalization does to a small pool. M3's recency and relevance both normalize to exactly 0, because it
is the pool's minimum on both — not because it is old in absolute terms. Normalization measures a memory **against its
competition**, never against a fixed scale. In a pool of three, one memory always scores 0 on each dimension
and one always scores 1.

### Tuning

| Symptom                                           | Knob                                            | Direction                                                             |
|---------------------------------------------------|-------------------------------------------------|-----------------------------------------------------------------------|
| results skew recent, miss older relevant memories | `AlphaRecency`                                  | lower — it defaults to 0.05 for this reason                           |
| stale memories crowd the top                      | `RecencyTau`                                    | shorten from 30 days                                                  |
| a `CRITICAL` memory never surfaces                | `AlphaImportance`                               | raise, or check the memory's `importance` was set at all              |
| top hits are near-duplicates                      | `MmrReordererOptions.Lambda`                    | lower toward 0.5                                                      |
| one leg dominates                                 | fuser `Weights`, or switch to `InterleaveFuser` | give each leg's top pick a guaranteed slot                            |
| keyword scores swamp vector scores                | keep `ReciprocalRankFuser`                      | it reads ranks and ignores magnitudes, which is why it is the default |
| relevance ordering is right but scores look wrong | check which stages ran                          | `Reranked` is null when no model ran; the scale differs per pipeline  |

Re-tune against a corpus, not against intuition. `AlphaRecency = 0.05` came from measuring nDCG@10 on LoCoMo; every
value above it traded relevance for freshness.

### What decay means

Recency measures **attention, not truth.** A physics fact losing recency during a database session is a true statement
about relevance, not a demotion of physics. When a physics question arrives, relevance brings it straight back, and the
access refreshes the clock.

At α = 0.05, a timeless fact cold for a year loses about five percent of its score.

### Which operations move the clock

The clock advances when **the agent's intent** selected the memory, never when the system offered it.

| Operation                                            | Clock         |
|------------------------------------------------------|---------------|
| `recall` — you asked, this answered                  | **refreshes** |
| `reclaim` — you named the id                         | **refreshes** |
| `recollect` — an enumeration you mostly discard      | no            |
| `neighbours` on retain — the server offered them unbidden | no       |

Refreshing on the last two would record accesses that never happened and inflate memories nobody used.

### Known gap: access is not verification

A memory recalled weekly for two years looks maximally fresh and may have been wrong for eighteen months. Recency tracks
attention, not correctness.

A staleness term — time since last **verified**, applied only where `content_time` is open — is a candidate. Einstein
would be exempt by construction, because his `content_time` is closed.

---

## 6. Quick reference

**Choosing a type**

```
┌────────────◇───────────┐
│                        │
│   Asserts something    │
│  that could be false?  │
│                        │
└────────────◇───────────┘
             │
             │
             ├─────────────────────────────╮no
          yes│                             │
             ▼                             ▼
┌────────────────────────┐    ┌────────────◇───────────┐
│                        │    │                        │
│          FACT          │    │   Is it a question?    │
│                        │    │                        │
└────────────────────────┘    └────────────◇───────────┘
                                           │
                                           │
             ╭───────────────────────────no┤
          yes│                             │
             ▼                             ▼
┌────────────────────────┐    ┌────────────────────────┐
│                        │    │                        │
│     OPEN_QUESTION      │    │       PREFERENCE       │
│                        │    │                        │
└────────────────────────┘    └────────────────────────┘
```

**Before you retain**

1. Can I name what would show this wrong? If not, sharpen the claim.
2. Did I run that check? If not, store what I did check, or file an `OPEN_QUESTION`.
3. Is the attribution inside the content, where it belongs?
4. Would a reader six months out understand this with no other context?
5. Do the parts die together? If not, split them.
6. Did I `recall` first, if storing a duplicate would actually matter here?

**Grounding.** The retrieval mechanics come from *Generative Agents: Interactive Simulacra of Human Behavior* (Park et
al., 2023) — the memory stream, the recency-importance-relevance score, and reflection as a process rather than a record
type. The type model does not. That paper's agents hold only observations, and those observations are ground truth
injected by the simulation, so its uniform memory stream costs nothing. Kontext holds durable knowledge that an agent
asserts about a real world, and earns the same uniformity by refusing to store what was not checked.
