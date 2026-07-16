---
title: Kontext Memory Model v2 — Typed, Evolvable, Event-Sourced
status: draft
authors: [sergio]
date: 2026-06-12
tags: [memory, contracts, event-sourcing, agent-memory, protobuf]
related: [2026-06-12-kontext-restructure]
---

> Input: today Kontext stores exactly ONE message contract in KurrentDB (`FactRetained` v1).
> This design replaces it with a typed, versioned, protobuf-defined memory model, informed by
> [Hindsight](https://hindsight.vectorize.io) ([paper](https://arxiv.org/pdf/2512.12818))
> and shaped around what an event store does better than a vector database.
>
> The isolation unit is the **Memory Shard** (`shard`). The shared payload embedded in every
> memory type is the **`MemoryBody`** (field `body`). Both names are locked.

## The idea in one paragraph

A memory is an **immutable protobuf event** in a per-shard KurrentDB stream. There are
**four memory types**, each its own event type with a shared `MemoryBody`. Memories are never
mutated — they are **revised, superseded, or retracted by later events**, and indexes fold
the stream into current state. Entities live in a **catalog** — their own event-sourced
registry per shard. The agent — not Kontext — is the intelligence that extracts entities
and synthesizes new memories; Kontext is the deterministic cataloger and stays LLM-free.

## Concepts

| Concept             | What it is                                                                                   | Stored where                         |
|---------------------|----------------------------------------------------------------------------------------------|--------------------------------------|
| **Memory**          | One typed, immutable knowledge unit (fact / experience / opinion / observation)              | `kontext-memory-{shard}` stream      |
| **Memory Shard**    | Isolation unit — one agent's/project's memory space; recall never crosses shards             | stream suffix + index namespace      |
| **Entity**          | A canonical thing memories are about (service, person, concept…), with aliases               | `kontext-catalog-{shard}` stream     |
| **Source**          | Typed provenance of a memory: a KurrentDB event, another memory, or a URI                    | field on every memory                |
| **Lifecycle event** | Revision / retraction / merge — changes the *status* of earlier events without mutating them | same streams                         |
| **Shard registry**  | The record that a shard exists; a home for per-shard config later                            | `kontext-shards` stream              |

## Memory types (epistemic roles, from Hindsight)

Hindsight organizes memory into four logical networks — world facts, experiences, observations,
and beliefs. Kontext's four memory types map onto them 1:1.

| Event type               | Role                                                     | Lifecycle                          |
|--------------------------|----------------------------------------------------------|------------------------------------|
| `FactRetained`           | Objective statement about the world                      | superseded by newer facts          |
| `ExperienceRecorded`     | First-person episode — what the agent did, what happened | append-only                        |
| `OpinionFormed`          | Subjective judgment **with confidence 0..1**             | revised as confidence shifts       |
| `ObservationSynthesized` | Neutral entity summary **derived from other memories**   | regenerated; must carry provenance |

## Contracts (protobuf)

Rules first, schema second:

- Contracts are **defined in `.proto`** — the schema is the law; no anonymous shapes.
- **Encoding on the wire**: canonical **protobuf JSON** initially (inspectable in the
  KurrentDB UI), with binary protobuf as a flip-of-a-switch later — same schema either way.
- **Event metadata** carries the schema id: the fully-qualified message name, e.g.
  `{"schema": "kurrent.kontext.memory.v2.FactRetained"}`. Readers dispatch on it.
- **Evolution**: field numbers are forever — never reuse, never renumber; removed fields go
  to `reserved`; additive `optional` fields only within a version; breaking change ⇒ new
  message in a new package version (`v3`); new memory kinds ⇒ new top-level messages.

```proto
syntax = "proto3";

package kurrent.kontext.memory.v2;

import "google/protobuf/timestamp.proto";

// ============================================================
// Shared building blocks
// ============================================================

// Common payload embedded as field 1 in every memory event.
message MemoryBody {
  string memory_id  = 1;            // ULID, server-generated; identity for revise/retract/supersede
  string shard      = 2;            // isolation unit
  string content    = 3;            // the natural-language memory text (indexed)
  repeated string keywords = 4;     // recall hints, may be empty
  repeated EntityRef entities = 5;  // what this memory is about (catalog refs)
  repeated Source sources = 6;      // provenance, may be empty for asserted knowledge
  Temporal temporal = 7;
  Attribution attribution = 8;      // omitted when unknown — never empty strings
  repeated string supersedes = 9;   // memory_ids this memory replaces (preserved, not hidden)
}

message Temporal {
  google.protobuf.Timestamp recorded_at    = 1; // when stored — ALWAYS set, server clock
  google.protobuf.Timestamp occurred_start = 2; // when it became true (optional)
  google.protobuf.Timestamp occurred_end   = 3; // interval end (optional; open interval if unset)
}

message Attribution {
  string agent      = 1;            // e.g. "claude-code"
  string session_id = 2;
}

// Reference to a canonical entity in the shard's catalog.
// The entity's canonical TYPE lives in the catalog, not here — see Entities below.
message EntityRef {
  string entity_id = 1;             // canonical slug, e.g. "order-service"
  string mention   = 2;             // surface form as written, e.g. "the Order Service" (optional)
}

// Typed provenance.
message Source {
  oneof source {
    EventRef event   = 1;           // a KurrentDB event — exact, replayable provenance
    string memory_id = 2;           // another memory
    string uri       = 3;           // external (PR, doc, ticket)
  }
}

message EventRef {
  string stream   = 1;
  uint64 revision = 2;
}

enum Outcome {
  OUTCOME_UNSPECIFIED = 0;
  SUCCESS             = 1;
  FAILURE             = 2;
  MIXED               = 3;
}

enum EntityType {
  ENTITY_TYPE_UNSPECIFIED = 0;
  PERSON                  = 1;
  ORGANIZATION            = 2;
  SERVICE                 = 3;     // dev-domain addition to Hindsight's set
  LOCATION                = 4;
  PRODUCT                 = 5;
  CONCEPT                 = 6;
  OTHER                   = 7;
}

// ============================================================
// Memory events — stream: kontext-memory-{shard}
// ============================================================

message FactRetained {
  MemoryBody body = 1;
}

message ExperienceRecorded {
  MemoryBody body = 1;
  Outcome outcome = 2;
  string task     = 3;              // what the agent was trying to do (optional)
}

message OpinionFormed {
  MemoryBody body  = 1;
  float confidence = 2;             // 0..1, REQUIRED
  repeated string basis = 3;        // memory_ids the judgment rests on (optional)
}

message ObservationSynthesized {
  MemoryBody body  = 1;
  string entity_id = 2;             // the subject — REQUIRED, must exist in catalog
  repeated string derived_from = 3; // memory_ids — REQUIRED non-empty; no provenance, no observation
}

// ============================================================
// Lifecycle events — same stream as the memory they affect
// ============================================================

message MemoryRevised {
  string shard    = 1;
  string revises  = 2;              // memory_id — REQUIRED
  string content  = 3;              // new text (unset = unchanged)
  optional float confidence = 4;    // new confidence (opinions only)
  repeated string keywords = 5;     // full replacement when present
  google.protobuf.Timestamp recorded_at = 6;
  Attribution attribution = 7;
}

message MemoryRetracted {
  string shard    = 1;
  string retracts = 2;              // memory_id — REQUIRED
  string reason   = 3;
  google.protobuf.Timestamp recorded_at = 4;
  Attribution attribution = 5;
}

// ============================================================
// Catalog events — stream: kontext-catalog-{shard}
// ============================================================

message EntityRegistered {
  string shard        = 1;
  string entity_id    = 2;          // canonical slug — identity, immutable
  string display_name = 3;          // "OrderService"
  EntityType type     = 4;
  repeated string aliases = 5;      // initial known aliases
  google.protobuf.Timestamp recorded_at = 6;
  Attribution attribution = 7;
}

message EntityAliasAdded {
  string shard     = 1;
  string entity_id = 2;
  string alias     = 3;
  google.protobuf.Timestamp recorded_at = 4;
  Attribution attribution = 5;
}

// The event-sourced answer to entity-resolution mistakes: corrections are recorded,
// not silently applied. Indexes re-fold; merged id becomes an alias of the survivor.
message EntityMerged {
  string shard            = 1;
  string into_entity_id   = 2;      // survivor
  string merged_entity_id = 3;      // becomes an alias of the survivor
  string reason           = 4;
  google.protobuf.Timestamp recorded_at = 5;
  Attribution attribution = 6;
}

// ============================================================
// Shard registry — stream: kontext-shards
// ============================================================

// Shards are EXPLICITLY provisioned: a memory write to an unregistered shard is rejected.
// `display_name` and future additive fields make this the home for per-shard config.
message ShardRegistered {
  string shard        = 1;          // canonical id — identity, immutable
  string display_name = 2;          // human label (optional)
  google.protobuf.Timestamp recorded_at = 3;
  Attribution attribution = 4;
}
```

## Shards — explicit provisioning

A Memory Shard must be **registered before any memory is written to it**. Registration is an
explicit `ShardRegistered` event in the `kontext-shards` registry stream; writing a memory to
an unregistered shard is rejected. Rationale: the registry is a single place to enumerate
shards and the natural home for per-shard configuration later (mirroring the KurrentDB
plugin's workspace registry), and explicit creation makes "which shards exist" a first-class
answerable question rather than an emergent side effect of writes.

The `default` shard is **system-seeded** (registered on first startup) so v1 compatibility and
zero-config single-shard usage keep working — see [Compatibility](#compatibility-with-v1).

## Entities — identification IS cataloging

How Hindsight does it (from the paper): their LLM extracts entity *mentions* during fact
extraction (typed PERSON/ORGANIZATION/LOCATION/PRODUCT/CONCEPT/OTHER), then resolves each
mention to a **canonical entity** by maximizing weighted similarity — string distance
(Levenshtein) + co-occurrence with other entities + temporal proximity:
`ρ(m) = argmax[α·sim_str + β·sim_co + γ·sim_temp]`. Each canonical entity then induces
graph edges between all memories mentioning it.

The Kontext adaptation — deterministic cataloging, no LLM in the server:

1. **The agent supplies mentions** at retain time — the calling LLM is a better entity
   extractor than any string heuristic. Each mention may carry an **optional `EntityType`
   hint**. That hint is a **retain-input field only** — used solely to type a newly
   auto-registered entity. It is **not** stored on `EntityRef`: the canonical type lives once
   in the catalog (`EntityRegistered.type`), so persisting it per memory would be redundant.
   Illustrative retain input (API shape, not a stored event):
   `{ "mention": "the Order Service", "type": "SERVICE" }`.
2. **The server resolves deterministically** (within an already-registered shard):
   - normalize the mention to a slug (lowercase, NFKC, non-alphanumerics → `-`, collapse)
   - look the slug up in the **alias map** folded from the catalog stream
     (`EntityRegistered.aliases` ∪ `EntityAliasAdded` ∪ `EntityMerged` redirects)
   - hit → use the canonical `entity_id`; miss → append `EntityRegistered` (auto-register,
     typed from the hint when present, else `ENTITY_TYPE_UNSPECIFIED`) and use the new id
3. **Corrections are events.** When "ord-svc" and "order-service" turn out to be the same
   thing, `EntityMerged` records it — the indexes re-fold, the merged id becomes an alias,
   and the *audit trail of the resolution itself* exists. Hindsight's argmax mis-resolves
   silently; ours mis-resolves visibly and reversibly.
4. Entity terms get a dedicated boosted index field → entity-scoped recall now, real graph
   traversal later (the catalog + entity refs ARE the node/edge list when we want it).

> Note the asymmetry: **shards are explicitly provisioned; entities auto-register.** A shard
> is a deliberate isolation boundary an operator opens; an entity is an emergent consequence of
> what the agent writes within that boundary.

## Sources — where we beat Hindsight

Hindsight has almost no provenance: facts are extracted from transcripts that are gone
after ingestion; observations reference facts internally, and that's the extent of it.
Kontext sits on a durable, addressable event store — `Source.event` is an exact
`stream@revision` pointer into the source of truth. Every memory can answer "based on
what?" with a replayable reference. `derived_from` on observations and `basis` on opinions
extend the same property to derived knowledge: the agent's full epistemic chain —
believed what, when, derived from what — is queryable by construction.

## Supersession — preserve, don't hide (matching Hindsight)

The open question was whether `supersedes` should auto-retract superseded memories from the
indexes or merely down-rank them. We checked what Hindsight does and match it:

**Hindsight neither deletes nor down-ranks superseded knowledge — history is preserved.**
After every retain, a background consolidation engine groups related facts against existing
**observations**, **refines beliefs rather than overwriting them**, and on contradiction
**captures the journey** ("User *was* a React enthusiast but *has since switched* to Vue")
instead of silently replacing the prior belief. Each observation carries a computed
**freshness trend** — `new` / `strengthening` / `stable` / `weakening` / `stale` — derived
from when its supporting evidence arrived.

The Kontext mapping:

- **Superseded memories are preserved and remain recallable** — never auto-retracted, never
  artificially down-ranked. The successor wins on recency/relevance; the predecessor stays
  reachable, and the `supersedes` / `supersededBy` relationship is **visible** in results so
  the agent can see the chain (and answer "what did I believe before?").
- **Belief evolution lives at the observation layer.** A refining `ObservationSynthesized`
  *supersedes* its predecessor and should **capture the journey**, not silently replace it.
  Event-sourcing preserves the full chain by construction — no overwrite is even possible.
- **Freshness trend is a derived recall signal**, not a stored field. Kontext computes it
  deterministically from the recency distribution of an observation's `derived_from` /
  supporting memories (temporal statistics — no LLM), so it stays inside the LLM-free
  promise. Values mirror Hindsight's: `new` / `strengthening` / `stable` / `weakening` / `stale`.
- **`MemoryRetracted` remains the sole explicit hide primitive.** "Newer info exists"
  (supersede) and "this was wrong / is gone" (retract) are distinct epistemic acts and stay
  distinct operations.

The one deliberate divergence: Hindsight's consolidation engine is **server-side (LLM)**;
Kontext's is **agent-side** (see Reflection). We match the *semantics* — preserve history,
refine observations, surface freshness — not the server machinery.

## Recall implications

Index document gains: `memoryType`, `shard`, `entityIds`, `confidence`, `occurredStart/End`,
`supersededBy`, `retracted`. Recall gains, in order of cheapness:

1. Type + shard filters — `RecallAsync(query, types: [Opinion], shard: "default")`
2. Entity-scoped recall via the boosted entity field (graph-lite)
3. Temporal range over the occurrence interval (Hindsight-style: interval overlap, not point)
4. Fold-aware results — retracted never surface; revised surface once in latest shape;
   superseded remain recallable, with the `supersededBy` relationship surfaced (preserve,
   not hide — see Supersession)
5. Confidence-weighted ranking for opinions
6. Freshness trend on observations (`new`/`strengthening`/`stable`/`weakening`/`stale`),
   computed at fold/recall time from supporting-evidence recency

Same RRF + cross-encoder pipeline — Hindsight independently converged on the identical
retrieval architecture (their four channels: semantic, BM25, graph spreading-activation,
temporal; ours: the first two today, entity + temporal via the new fields).

## Reflection — the Kontext twist

Hindsight runs server-side LLM summarization (per-entity observations regenerate in
background tasks). Kontext's promise is **no LLM in the box** — and every caller IS an LLM:
the shipped skill + tool descriptions instruct agents to periodically recall → synthesize →
retain `ObservationSynthesized`/`OpinionFormed` with provenance. Zero server machinery;
the contracts already carry it. A built-in reflect step (optional `IChatClient` background
service) remains possible later without contract changes.

## Compatibility with v1

Existing `FactRetained` v1 events (`fact`/`keywords`/`sourceEvents`/`sessionId`/`retainedAt`)
are **upcast at read time** — never rewritten:

| v1                               | v2                                                          |
|----------------------------------|-------------------------------------------------------------|
| `fact`                           | `body.content`                                              |
| `keywords`                       | `body.keywords`                                             |
| `sourceEvents` (`"stream::rev"`) | `body.sources[].event` (parsed)                             |
| `retainedAt`                     | `temporal.recorded_at` (= `occurred_start`)                 |
| `sessionId` (empty = absent)     | `attribution.session_id` (omitted when empty)               |
| —                                | `memory_id` := `{stream}@{revision}`; `shard` := `default`  |

Detection: v1 events have no `schema` metadata key. The `default` shard is system-seeded so
upcast v1 facts resolve to a registered shard.

## Out of scope (deliberately)

- Graph storage/traversal engine — catalog + entity refs are the seed; traversal is a later bet
- Server-side reflection — agent-side first
- Decay/forgetting policies — `MemoryRetracted` is the primitive; policy later
- Shards as security boundaries — isolation yes; ACLs remain `IKontextStreamAccessChecker`
- Per-shard configuration — `ShardRegistered` is the home; concrete config fields come later
- Per-topic substreams — single `kontext-memory-{shard}` stream now; topic partitioning is a
  stream-naming change with zero schema impact, addable later without a contract change
- Hindsight's causal links and disposition/behavioral profiles — noted, not adopted yet

## Working decisions (under discussion — NOT final)

| Question              | Decision                                                                                    |
|-----------------------|---------------------------------------------------------------------------------------------|
| Isolation-unit name   | **Memory Shard** (`shard`); streams `kontext-memory-{shard}` / `kontext-catalog-{shard}`    |
| Shared-payload name   | **`MemoryBody`** (field `body`), replacing `Envelope`                                       |
| Shard provisioning    | **Explicit** — `ShardRegistered` in `kontext-shards`; `default` system-seeded               |
| `supersedes` behavior | **Preserve, don't hide** — match Hindsight; freshness trend; retract is the only hide       |
| Entity type hint      | **Optional `EntityType` as retain input only** — not stored on `EntityRef`                  |
| Memory stream layout  | **Single `kontext-memory-{shard}` stream** per shard                                        |
| Wire encoding         | **Canonical protobuf JSON** initially (UI-inspectable); binary protobuf later — same schema |

## Discussion log

**2026-06-16** — Working session. Doc remains `draft`; nothing here is accepted.

Names the user locked explicitly: isolation unit **Memory Shard**, shared payload
**`MemoryBody`** (field `body`) replacing `Envelope`.

Ideas explored and currently leaning a particular way, but still **open**: explicit shard
provisioning; `supersedes` preserves history (Hindsight-style) with a freshness-trend signal;
entity `EntityType` as retain-input only; single stream per shard.

Still under active discussion: how Kontext's memory types map onto Hindsight's, the
agent-facing vs system-derived distinction, and how tags/entities are discovered.
