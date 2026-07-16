---
title: Agentic Memory Systems — Comparative Survey (Mnemosyne, claude-mem, Memorizer, Hindsight)
type: analysis
date: 2026-07-03
author: sergio
tags: [kontext, memory-model-v2, agent-memory, survey, api-design]
related: [2026-06-12-memory-model-v2]
scope: external systems — models, APIs, capabilities, mistakes, best features
---

## Summary

Four agentic memory systems surveyed by one research agent each (full reports in the
appendices, verbatim): **Mnemosyne** (Python/SQLite local-first library, 25 MCP tools),
**claude-mem** (TypeScript hook-driven observation compressor for coding agents, SQLite/FTS5 →
newer Postgres event-sourced runtime), **Memorizer** (Petabridge, .NET/Postgres+pgvector MCP
server with workspaces/projects and event-sourced versioning), and — added later the same day —
**Hindsight** (Vectorize, Python/FastAPI + Postgres/pgvector, the paper-backed reference system
whose four memory networks inspired Kontext's memory types; Appendix D). Hindsight headlines:
bi-temporal fields on every unit + temporal retrieval as a parallel channel, evidence-grounded
observations (`source_memory_ids` + exact quotes + computed freshness), invalidation-as-archive
(soft-deleted rows *move* tables, the hot path carries no status predicate), per-stage score
transparency — and as warts: "three verbs" marketing over ~60 REST endpoints/31 MCP tools, a
vestigial Opinion network (schema-mandated `confidence_score` for a type recall excludes —
paper–product drift), async retain that returns no memory id, and identifier naming churn
paid for in migrations.

Headline for the Kontext memory-model-v2 iteration: the survey **validates every ruling made on
2026-07-03** (no containers, four operations, server-minted neutral identity, tags agent-facing
with keywords demoted to internal enrichment), supplies **three field-level additions to
consider for `MemoryBody`** (importance as a ranked signal, internal enrichment outputs,
origin/veracity — which our typed `Source` already encodes), and exposes **one gap in our
contracts** (forget needs a two-level answer: soft retract vs hard delete). The recurring
industry disease is **tool sprawl and duplicate surfaces** — all three systems suffer it; the
4-operation surface is the right instinct.

## The comparison matrix

| Dimension | Mnemosyne | claude-mem | Memorizer | Kontext v2 (rulings applied) |
|---|---|---|---|---|
| Unit of memory | episodic row + working memory + SPO triples + canonical facts (4+ overlapping kinds) | AI-compressed "observation" + session summaries + raw prompts | one `Memory` (JSON content + text) with type enum ×2 (legacy string + enum) | 4 typed protobuf events sharing `MemoryBody` |
| Identity | server UUID (docs contradict: `mem_abc123` in examples) | **local autoincrement int** — their own wart; forces composite-key dedupe on export | server `Guid` in strongly-typed `MemoryId` | server-minted; guid/record-id vs log_position OPEN (ruling C) |
| Importance | `importance` 0..1, default 0.5, agent-supplied, **20% rank weight** | none stored | `Confidence` 0..1 (`UnitInterval` struct) — stored but **unused in ranking** | to decide: add `importance` 0..1 to MemoryBody |
| Origin/veracity | `veracity` enum (stated/inferred/tool/imported/unknown) → score multiplier ×0.5–1.0 | `sourceType` on events (hook/worker/provider/server/api) | freeform `Source` string | typed `Source` oneof (event/memory/uri) — encodes the same signal structurally |
| Tags/keywords | tags inconsistent (REST yes, schema no); metadata bag | no user tags; **AI-extracted `facts`/`concepts` columns** (filterable) | tags GIN-indexed; **soft-boost in search, never hard-filter**; in the metadata embedding | tags agent-facing (tags-spec); keywords → internal enrichment (ruling B) |
| Containers | five-dimensional scoping (bank/session/scope/channel/author) — a wart | one `project` TEXT column (≈ a tag) | Workspaces + Projects + well-known Unfiled; polymorphic owner | **none** (ruling A); `project:` tag covers claude-mem's column |
| Relationships | 3 overlapping graph systems (edges, triples, memoria_kg) — a wart | none (timeline substitutes) | typed directed edges, versioned, bidirectional similar-to | supersedes + sources + entity co-mention (implicit graph); explicit links deferred |
| Lifecycle | invalidate→`superseded_by` tombstone; tiered degradation (fade, never delete); forget = hard delete | **no forget at all** (their #1 wart); append-only + session summaries | event-sourced versions w/ typed change events + diff stats; revert-as-new-version; archive (soft) + delete (hard, purges history) | revise/retract events; preserve-don't-hide; hard-delete story OPEN |
| Search | hybrid vec+FTS+importance, per-query tunable weights, point-in-time recall, tier multipliers | FTS5 3-layer progressive disclosure: index-with-token-costs → timeline → get-by-ids | pgvector+FTS **RRF k=60**, weighted tsvector (title>tags>body), metadata-only embedding, **checked-in eval harness (MRR/NDCG)** | embed + one DuckDB SQL (vector exact + lexical + tags + RRF) per vector-fts analysis |
| Enrichment | in-process LLM chain (sleep consolidation, fact extraction — cloud model default contradicts local-first) | **all async in background worker** (cheap model), atomic event+outbox+job; structured extraction | deliberately narrow: embeddings + background title-gen + system index memories; rest agent-side | LLM-free server; agent-side reflection; enrichment fields reserved, machinery later |
| Provenance | multi-field but freeform | strong capture provenance (3 session ids, tool, files, correlation) but no model/prompt-version stamp | thin: freeform `Source`/`ChangedBy` strings | typed Source refs — strongest of the four |
| Result shapes | undocumented tool returns | priced index rows (`~155 tokens`) + workflow-enforcing `__IMPORTANT` tool | **prose strings with emoji** — their #1 wart | structured content, thin hits (established this week) |

## Findings mapped to our decisions

### Rulings validated by the survey

1. **A (no shards/workspaces).** Mnemosyne's five overlapping scoping dimensions are a
   documented mess; Memorizer's workspaces+projects are its heaviest machinery; claude-mem —
   the most successful of the three at its niche — scopes by a single `project` string column,
   which is functionally *a tag*. Our tags-only answer is claude-mem's shape with better typing.
2. **F (four operations).** Tool sprawl is the universal disease: Mnemosyne ships 25 tools (28
   by its own frontmatter count); claude-mem deleted 9 overlapping tools in an 88% code
   reduction and then *re-grew* a dual surface (`memory_*` aliases beside `observation_*`);
   Memorizer ships 22 with docs referencing tools that don't exist. `retain / recall / forget /
   search` is the discipline none of them kept.
3. **C (identity, leaning record-id).** Nobody exposes a storage position as public identity,
   and claude-mem's local autoincrement ids are its self-admitted portability wart. Server-minted
   globally-unique id is table stakes; log_position remains the *internal* join key. This also
   satisfies the "never couple the public model to KurrentDB concepts" constraint.
4. **B (tags public, keywords internal).** claude-mem's AI-extracted `facts`/`concepts` columns
   are exactly the "keywords as internal enrichment" instinct: not agent-managed, but indexed
   and filterable. Memorizer's title>tags>body weighting and metadata-only embedding show tags
   pulling ranking weight without being hard filters.

### Additions to consider for `MemoryBody` / the contracts

1. **`importance` (0..1) on `MemoryBody`** — but only if it ranks. Mnemosyne proves it works as
   a 20% rank term; Memorizer proves that storing it *without* using it is dead weight. With
   the DuckDB recall SQL, importance-as-rank-term is one expression. (Distinct from
   `OpinionFormed.confidence`, which is epistemic, not priority.)
2. **Origin/veracity signal** — Mnemosyne's best idea (stated/inferred/tool/imported/unknown as
   a score multiplier + `get_contaminated()` audit). We largely get it structurally: a memory
   whose `Source.event` is set is tool-grounded; `Source.memory_id` = derived;
   `Source.uri` = imported; no sources = asserted. Decision: derive the signal from typed
   sources at fold time rather than adding a field — but write that mapping down in the design.
3. **Reserved enrichment fields** — sentiment, summary, internal keywords/concepts: reserve
   optional fields (or an `Enrichment` message) now; field numbers are forever, machinery later.
   claude-mem's architecture is the template when machinery comes: async worker off the hot
   path, cheap model, **atomic event+outbox+job** — which maps 1:1 onto appending enrichment
   events to a derived stream (our memory-storage-design rule: expensive/non-deterministic
   derivations become events).

### Gaps the survey exposes in our contracts

1. **Forget needs two levels.** claude-mem's missing forget is called a liability in its own
   report; Mnemosyne and Memorizer both ended up with soft (invalidate/archive) *and* hard
   (delete/purge) primitives. Our `MemoryRetracted` is the soft level. The hard level
   (privacy/GDPR erasure) in an event-sourced store means redaction/scavenge territory — the
   contracts should at least name the operation and its semantics, even if v1 implements
   retract-only.
2. **Session recap capability.** claude-mem's structured session summaries
   (`request/investigated/learned/completed/next_steps`) and Mnemosyne's sleep consolidation
   both answer "what happened while I was gone". Our model has `Attribution.session_id` but no
   recap concept; `ObservationSynthesized` (or `ExperienceRecorded`) can carry it agent-side —
   worth a line in the design's Reflection section rather than a new type.
3. **Result-shape discipline for recall.** Steal claude-mem's progressive disclosure: recall
   returns thin, *priced* hits (their index rows carry `~N tokens` estimates); expansion is a
   separate step (for us: the `query` tool / a get-by-ids). Memorizer's prose-string results
   are the definitive counterexample.
4. **Timeline as a retrieval primitive.** claude-mem's anchor±depth temporal neighborhood is a
   natural fit for an event-sourced memory (ORDER BY position around an anchor) and costs us
   almost nothing via the query surface.

### Warts catalog (what NOT to do, with the system that proves it)

- Tool sprawl / duplicate parallel surfaces (all three).
- Dead parameters — Memorizer's `minSimilarity` is documented, accepted, and ignored.
- Two type systems at once — Memorizer's `TypeLegacy` string beside `MemoryTypeEnum`.
- Docs that contradict the schema — Mnemosyne's tags/ids/scoring inconsistencies.
- No forgetting — claude-mem.
- Prose tool results — Memorizer's emoji strings.
- Local autoincrement identity — claude-mem.
- Marketing numbers vs measured numbers — Mnemosyne ("<1ms" claim vs 65ms median in its own docs).
- Container proliferation — Mnemosyne's 5 scoping dimensions.

### Best features shortlist (steal-worthy, ranked by fit)

1. Progressive disclosure with token-priced index rows (claude-mem).
2. Event-sourced versioning with typed change events + revert-as-new-version (Memorizer —
   independent convergence on our architecture).
3. Veracity-from-origin as a recall signal (Mnemosyne; ours derives from typed Sources).
4. Async enrichment via atomic event+outbox+job (claude-mem).
5. Search eval harness checked into the repo, gating ranking changes on MRR/NDCG (Memorizer).
6. Tiered degradation — memories fade (rank multipliers), never vanish (Mnemosyne; cousin of
   our preserve-don't-hide + freshness trend).
7. Timeline retrieval primitive (claude-mem).
8. Tag soft-boost vs hard-filter as a deliberate, documented ranking choice (Memorizer).

## Method

Three parallel research agents, one per system, common 10-section template (identity, memory
object, organization, lifecycle, API surface, search, enrichment, provenance, verdict, sources).
Sources: official docs sites, GitHub repos (raw source for Memorizer's tools/models — verbatim
C#), the live claude-mem plugin's MCP JSONSchemas. Fetched 2026-07-03. Each appendix carries its
own confidence notes; notable caveats: Mnemosyne's docs self-contradict (both readings reported),
claude-mem's docs lag its shipped v13 runtime, Memorizer claims are from the `dev` branch, not
executed.

---

# Appendix A — Mnemosyne (agent report, verbatim)

# Mnemosyne — Research Report

## 1. Identity & positioning

Mnemosyne (mnemosyne.site, github.com/AxDSan/mnemosyne, MIT, "Built by Abdias Moya", current docs version 3.8.0) is "The universal memory layer for any AI agent. SQLite-backed, sub-millisecond, zero dependencies. One pip install." Consumers are AI agents — "Works with Hermes, Claude Code, Cursor, Codex, OpenWebUI, OpenClaw + MCP" — with Hermes Agent as the first-class integration; apps can use the Python SDK directly. Architecture: an embedded, local-first, single-node Python library over one SQLite file (sqlite-vec for vectors, FTS5 for text, BAAI/bge-small-en-v1.5 384-dim embeddings via fastembed); self-hosted only ("Mnemosyne does not support distributed or clustered deployments"), with an optional MCP server (`mnemosyne mcp`, stdio or SSE) and a "legacy" FastAPI REST server. Multi-instance sync exists via checkpointed DeltaSync (`sync_to`/`sync_from`).

## 2. The memory object

Episodic memory table schema, quoted verbatim from `/memory-systems/episodic`:

| Column | Type | Description |
|---|---|---|
| rowid | INTEGER | Auto-increment primary key |
| id | TEXT | UUIDv4 unique identifier |
| content | TEXT | Full experience record |
| source | TEXT | Origin (user, tool, observation) |
| timestamp | TEXT | Application-level timestamp |
| session_id | TEXT | Session grouping key (default: 'default') |
| importance | REAL | 0.0–1.0 importance score (default: 0.5) |
| metadata_json | TEXT | Arbitrary JSON metadata blob |
| summary_of | TEXT | IDs of original Working Memory entries this summarizes |
| created_at | TIMESTAMP | Row creation time |
| recall_count | INTEGER | Number of times recalled (default: 0) |
| last_recalled | TIMESTAMP | Last recall time (default: NULL) |
| valid_until | TIMESTAMP | TTL expiry time |
| superseded_by | TEXT | ID of newer version |
| scope | TEXT | Visibility scope (default: 'global') |

Plus a `veracity` field (added via `ALTER TABLE` migration; enum `stated | inferred | tool | imported | unknown`) and a degradation `tier` (1/2/3). Embeddings live in a sibling `vec_episodes` table (sqlite-vec, `int8[384]`); FTS in `fts_episodes`.

- **Kinds/tiers**: Working Memory (hot, ≤10K entries, 24h TTL), Episodic (long-term), Semantic/TripleStore (subject–predicate–object triples with `valid_from`, `valid_until`, `confidence`, `source`), Scratchpad (session-bound notes), plus MEMORIA structured tables (`memoria_facts`, `memoria_timelines`, `memoria_kg`, `memoria_instructions`, `memoria_preferences`) and v3.8 "canonical facts" (owner-scoped single source of truth with slot/category/history).
- **Identity**: server-minted; `id TEXT` documented as UUIDv4, but REST examples show `"id": "mem_abc123"` — the two formats are never reconciled. `remember()` "Returns the generated memory ID."
- **Salience**: `importance: float`, 0.0–1.0, default 0.5, client-supplied, used directly as a ranking signal (20% default weight). `veracity` acts as a confidence multiplier on recall score (stated 1.0x, inferred 0.7x, tool 0.5x, imported 0.6x, unknown 0.8x). Triples carry `confidence: float = 1.0`.
- **Temporal model**: `timestamp` ("application-level") vs `created_at` (row creation) vs `last_recalled`/`recall_count` (access tracking) vs `valid_until` (business validity/TTL). Triples add `valid_from`/`valid_until` bitemporal validity with `as_of` point-in-time queries. No "occurred_at" distinct from `timestamp` is documented.

## 3. Metadata & organization

- **Tags/keywords**: REST `/store` accepts `"tags": ["preferences", "ui"]`, but no `tags` column exists in the documented schema and the SDK `remember()` has no tags parameter — inconsistent. `metadata: dict` is the general-purpose bag. `topic` is a recall filter.
- **Entities**: opt-in extraction (`extract_entities=True`) "via regex + Levenshtein matching," stored as triples/annotations.
- **Scoping is five-dimensional** (a lot): `bank` (isolated SQLite file per project, `data_dir/banks/<name>/`), `session_id` (working-memory isolation, per-user isolation pattern), `scope` (`"session"` or `"global"`), `channel_id` ("cross-session shared memory"), `author_id`/`author_type` (`"human" | "agent" | "system"`), plus a separate shared-surface DB for cross-agent memory (`shared_remember/…`) and an `owner` for canonical facts. Multi-tenancy = separate DB files.
- **Relationships**: `superseded_by` (versioning link); `summary_of` (consolidation lineage); explicit memory-to-memory graph edges via `mnemosyne_graph_link(source_id, target_id, relationship, weight)` with multi-hop BFS traversal (`graph_query`, `max_hops`, `min_weight`); SPO triples for entity relationships. Note: "The TripleStore does not currently support multi-hop graph traversal" — traversal only works on the memory-edge graph, not the triple graph.

## 4. Lifecycle

- **Create**: `remember()` always writes to Working Memory; episodic entries "are created during sleep consolidation, not directly by the agent."
- **Update**: `update(memory_id, content, importance)` — in-place mutation, no revision history documented.
- **Versioning/supersede**: `invalidate(memory_id, replacement_id)` "Sets the superseded_by field... signalling that it is no longer the canonical version" — soft tombstone preserving history. Canonical facts support `include_history=True`. Triples auto-invalidate: `add()` "Auto-invalidates previous triples with the same (subject, predicate) pair."
- **Forget = hard delete**: `mnemosyne_forget`: "Permanently delete a memory by ID." No bulk delete ("iterate through results").
- **TTL/decay**: `valid_until` per memory; Working Memory 24h TTL; recall-side exponential recency decay (`temporal_halflife`). Tiered degradation replaces expiry: Tier 1 (0–30d, full detail, 1.0x weight) → Tier 2 (30–180d, LLM-compressed ~400 chars, 0.5x) → Tier 3 (180d+, entity-extracted ~250 chars, 0.25x, needs "4x stronger match" to surface); "Old memories never get deleted."
- **Consolidation**: `sleep()` — selects WM entries older than TTL/2, groups by `source`, summarizes (LLM fallback chain: host LLM → remote OpenAI-compatible API → local MiniCPM5-1B GGUF → AAAK lossy text substitution), promotes summary to episodic with `summary_of`, marks/evicts originals, logs to `consolidation_log`. Synchronous, explicit (opt-in `auto_sleep`). Deduplication: recall-time only (highest score wins); LLM conflict detection is a feature flag (`MNEMOSYNE_LLM_CONFLICT_DETECTION`, default false).

## 5. API surface

**MCP tools (25, v3.8.0)** — the tool-schema page provides parameter tables, not JSON Schema; representative verbatim entries:

> **1. mnemosyne_remember** — Store a durable memory
> | content | string | yes | · | importance | float | no (default: 0.5) | · | source | string | no (default: user) | · | scope | string | no (default: session) | · | valid_until | string | no | · | extract_entities | bool | no (default: false) | · | extract | bool | no (default: false) | · | metadata | dict | no | · | veracity | string | no (default: unknown) |

> **2. mnemosyne_recall** — Search memories by vector+FTS hybrid ranking
> | query | string | yes | · | limit | int | no (default: 5) | · | temporal_weight | float | no (default: 0.0) | · | query_time | string | no | · | temporal_halflife | float | no (default: 24) | · | vec_weight / fts_weight / importance_weight | float | no (default: null) |

> **3. mnemosyne_forget** — Permanently delete a memory by ID — `memory_id: string (required)`

Remaining 22: `mnemosyne_get`, `mnemosyne_update(memory_id, content?, importance?)`, `mnemosyne_validate(memory_id, action: enum[attest,update,invalidate,delete], validator?, new_content?, note?, bank: enum[private,surface])` ("collaborative ownership"), `mnemosyne_invalidate(memory_id, replacement_id?)`, `mnemosyne_import` (from JSON/Hindsight/Mem0), `mnemosyne_export(output_path)`, `mnemosyne_diagnose` ("PII-safe diagnostics"), `mnemosyne_stats`, `mnemosyne_sleep(all_sessions?, dry_run?)`, `mnemosyne_triple_add(subject, predicate, object, valid_from?)`, `mnemosyne_triple_query(subject?, predicate?, object?)`, `mnemosyne_graph_link(source_id, target_id, relationship, weight=0.5)`, `mnemosyne_graph_query(seed_memory_id, max_hops=2, edge_type?, min_weight=0.0)`, `mnemosyne_shared_remember(content, kind=meta, importance=0.8, veracity, metadata)`, `mnemosyne_shared_recall(query, limit=5)`, `mnemosyne_shared_forget(memory_id)`, `mnemosyne_shared_stats`, `mnemosyne_scratchpad_write/read/clear`, `mnemosyne_remember_canonical(content, importance, veracity, source, valid_until)`, `mnemosyne_recall_canonical(slot?, category?, substring?, limit=5, include_history=false)`. Tool return shapes are **not documented** on the tool-schema page.

**REST API (explicitly "Legacy FastAPI... not part of the BEAM architecture")**: `GET /` (status+counts), `POST /store` `{content, tags, importance, source}` → `{"id": "mem_abc123", "status": "stored"}`, `GET /recall?query=...&top_k=5` → `{results: [{id, content, score, tags}], total}`, `GET /session/{session_id}`, `GET /stats`, `DELETE /forget/{memory_id}` → `{"status": "forgotten", "id": ...}`, `PUT /update/{memory_id}`, `POST /consolidate` → `{status, consolidated_count, evicted_count}`. Errors: 400 INVALID_REQUEST / 404 NOT_FOUND / 500 INTERNAL_ERROR. OpenAPI at `/openapi.json`.

**Python SDK** (primary): `Mnemosyne(session_id, db_path, bank, author_id, author_type, channel_id)`; `remember`, `recall`, `get`, `get_context(limit=10)`, `get_stats`, `forget`, `update`, `invalidate`, `sleep`, `sleep_all_sessions`, `compress/decompress`, `detect_patterns`, `sync_to/sync_from`, `export_to_file/import_from_file`, `consolidation_log`, `get_contaminated`, plus `TripleStore.add/query/export_all/import_all` and stream/plugin subsystems. The Hermes plugin registers only 17 of the 25 tools (shared_* and validate/get are MCP-only).

## 6. Search/recall

- **Modes**: hybrid (default) = dense vector (sqlite-vec cosine) + FTS5 BM25 + importance; structured filters (`from_date`, `to_date`, `source`, `topic`, `author_id`, `author_type`, `channel_id`, `session_id`, veracity); direct lookup by id (`get`); triple lookup by S/P/O with `as_of`; graph BFS from a seed memory; canonical-fact lookup by slot/category/substring.
- **Ranking**, verbatim: `base_score = (vector_score * 0.5) + (fts_score * 0.3) + (importance * 0.2)` then `recency_decay = exp(-age_hours / half_life_hours); final_score = base_score * (0.7 + 0.3 * recency_decay)` — all three weights per-query tunable, plus `temporal_weight`/`temporal_halflife`/`query_time` (point-in-time "as of" recall), veracity multipliers (0.5x–1.0x) and tier multipliers (1.0/0.5/0.25). Access frequency (`recall_count`) is stored but not documented as a ranking signal.
- **Response shape**: list of dicts — "Each result dict includes keys like id, content, source, importance, timestamp, score, and tier (indicating working vs. episodic)."
- **Pagination**: none — `top_k`/`limit` only (default 5); `MNEMOSYNE_EP_LIMIT` (50000) caps candidates. Cursors/offsets: not documented.

## 7. Enrichment & classification

All enrichment runs in-process ("server-side" = the library), mostly opt-in:
- **Sleep summarization** (LLM): groups + summarizes WM entries; fallback chain host-LLM → remote API → local MiniCPM5-1B GGUF → AAAK deterministic text substitution (lossy, one-way, 30–50% reduction).
- **Fact extraction** (`extract=True`): "extract 2–5 structured factual statements via LLM" — default `MNEMOSYNE_EXTRACTION_MODEL: google/gemini-2.5-flash` via OpenRouter.
- **Entity extraction** (`extract_entities=True`): regex + Levenshtein, no LLM.
- **MEMORIA regex-mining**: automatic during `remember()` — populates facts/timelines/KG/instructions/preferences tables; retrieval "routes queries by ability type."
- **Tier-2 compression** (LLM ~400-char summaries) and **Tier-3 `_extract_key_signal()`** (heuristic sentence scoring by entity density: acronyms +3, tech terms +4, security +3, urgency +3...).
- **Conflict detection** (LLM, feature-flagged off), **SHMR** clustering+LLM reorganization (env-configured), **PatternDetector** (temporal/content/sequence, non-LLM), **session recap** via Hermes `on_session_end` hook ("Trigger consolidation, store conversation summary").
- **Not** server-side: importance scoring (client-supplied), tagging (no auto-tags), sentiment (not documented).

## 8. Provenance

Yes, multi-field: `source` (free text — "user", "tool", "slack"; also drives consolidation grouping), `veracity` (epistemic origin class), `author_id`/`author_type` (who wrote it), `session_id` + `channel_id` (conversation context), `metadata_json` (arbitrary), `summary_of` (which WM entries a consolidated memory derives from), `trust_tier` (SDK-only "trust tier label for memory provenance tracking"), triples carry `source`, `validate` records a `validator` and `note`. No document/URL source references or message-level conversation IDs are documented.

## 9. Verdict

**Warts** (all evidenced by internal doc contradictions):
1. **Three overlapping "graph" systems** — TripleStore SPO triples, `memoria_kg` triples, and `graph_link` memory edges — with split capabilities (BFS works on edges but "TripleStore does not currently support multi-hop graph traversal"). Redundant concepts, confusing surface.
2. **Contradictory scoring docs**: episodic page says the 20% weight is "Temporal proximity"; ranking/hybrid pages say it's `importance`; hybrid page lists defaults as `50.0/30.0/20.0` while env vars say `0.5/0.3/0.2`.
3. **Delete-vs-mark incoherence**: sleep docs say originals are "marked as consolidated (rows remain recallable)"; data-flow and SDK say "Eviction removes the original Working Memory entries"; tiered degradation says "no memory is ever deleted" while `forget` is a permanent hard delete and REST returns `evicted_count`.
4. **Schema drift across surfaces**: REST accepts `tags` that exist nowhere in the schema or SDK; IDs are "UUIDv4" in the schema but `mem_abc123` in every example; MCP uses `veracity` where the SDK uses `trust_tier`; tool count is "25" in prose, `tool_count: 28` in the same page's frontmatter; scratchpad is "a single mutable string... no history" on one page and an append-log of up to 1,000 entries on two others.
5. **Marketing vs measured numbers**: homepage claims "<1ms query latency" and "100% local, zero exfil, no API calls"; the docs' own tables say hybrid recall is 65ms median/180ms p99 (episodic median 85ms) and the default fact-extraction model is a cloud call (`google/gemini-2.5-flash` via OpenRouter). ~90 undocumented-interaction env vars is its own smell.

**Worth stealing**:
1. **Veracity as a first-class field** (`stated/inferred/tool/imported/unknown`) that multiplies recall score, plus `get_contaminated()` for auditing AI-inferred memories — directly maps to a protobuf enum + recall filter, and fights the real problem of inferences drowning out user statements.
2. **Supersede-not-delete lineage**: `invalidate(memory_id, replacement_id)` → `superseded_by`, `summary_of` tracking consolidation ancestry, and canonical facts with `include_history` — naturally event-sourced; an event-sourced store gets this almost for free.
3. **Tiered degradation instead of TTL deletion**: age-based compression with per-tier recall-weight multipliers ("Tier 3 needs 4x stronger match") — memories fade rather than vanish.
4. **Per-query tunable rank fusion** (`vec_weight/fts_weight/importance_weight/temporal_weight` + `query_time` for point-in-time recall) — lets the agent choose exact-match vs semantic vs recency per call without server redeploys.
5. **Bitemporal SPO triples** (`valid_from`/`valid_until`, `as_of` queries, auto-invalidation of the previous (subject, predicate) value) as a separate structured-fact channel next to fuzzy episodic memory.

## 10. Sources

- https://mnemosyne.site/en/ (homepage; fetched via curl with browser UA — site 403s the default fetcher)
- https://docs.mnemosyne.site/api/rest and /api/tool-schema (primary)
- docs.mnemosyne.site pages: /api/overview, /api/python-sdk, /api/hermes-plugin, /memory-systems/{working,episodic,semantic,scratchpad,temporal-graph}, /architecture/{beam-overview,system-design,data-flow,sleep-consolidation,aaak-compression,tiered-degradation,veracity-signal}, /retrieval/{hybrid-search,ranking}, /getting-started/{configuration,first-steps}, /security/access-control
- WebSearch corroboration: mnemosyne.site, github.com/AxDSan/mnemosyne, hermesatlas.com project page

**Confidence note**: All schemas/signatures above are quoted from the docs as fetched 2026-07-03. Not verified: exact MCP tool *return* shapes (not documented anywhere), the JSON-Schema form of tool inputs (docs give parameter tables only), Working Memory's own column list (only episodic is fully specified), whether `recall_count` feeds ranking, and any cursor pagination (none documented). Several docs contradict each other (§9); where they did, I reported both readings rather than picking one. Beware name collisions: mnemosy.ai, rand/mnemosyne, and an arXiv "Mnemosyne" paper are unrelated projects.

---

# Appendix B — claude-mem (agent report, verbatim)

# claude-mem (cmem) — Research Report

## 1. Identity & positioning

claude-mem is an open-source (Apache-2.0), TypeScript "persistent memory compression system built for Claude Code," marketed at cmem.ai as "the missing memory layer for agentic coding" — "Your agents finally remember." Consumers are coding agents: Claude Code first-class (as a plugin), plus Cursor, Windsurf, OpenCode, Codex CLI, Gemini CLI via a drop-in MCP server, with a paid sync layer ("CMEM Cloud", `mcp.cmem.ai/u/[uuid]` private MCP link, free in beta). Architecture: Claude Code lifecycle hooks capture tool executions → a local Bun/Express worker service compresses them via the Claude Agent SDK into structured "observations" in SQLite (`~/.claude-mem/claude-mem.db`, FTS5) with optional Chroma vector search; recall happens via context injection at SessionStart and via MCP search tools. A newer "server runtime" (visible in the current plugin, v13.x) adds a Postgres-backed event-sourced backend (`/v1/events` → outbox → generation jobs).

## 2. The memory object

Primary unit is the **observation** — an AI-compressed record of tool activity. Verbatim schema (docs.claude-mem.ai/architecture/database):

```sql
CREATE TABLE observations (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL,
sdk_session_id TEXT NOT NULL, claude_session_id TEXT, project TEXT NOT NULL,
prompt_number INTEGER, tool_name TEXT NOT NULL, correlation_id TEXT, title TEXT,
subtitle TEXT, narrative TEXT, text TEXT, facts TEXT, concepts TEXT, type TEXT,
files_read TEXT, files_modified TEXT, created_at TEXT NOT NULL, created_at_epoch INTEGER NOT NULL)
```

Three memory kinds: **observations**, **sessions** (`sdk_sessions`: id, sdk_session_id UNIQUE, claude_session_id, project, prompt_counter, status default 'active', created/completed/last_activity as TEXT + epoch INTEGER pairs), **session_summaries** (structured fields `request, investigated, learned, completed, next_steps, notes`), plus raw **user_prompts** (prompt_text, per prompt_number). Observation `type` enum: `decision, bugfix, feature, refactor, discovery, change`; the newer runtime adds a free-string `kind` ("default: manual") on manual adds. **Identity**: local SQLite `INTEGER PRIMARY KEY AUTOINCREMENT` minted by the DB — not globally unique (export/import must dedupe by composite keys, e.g. observations by `sdk_session_id + title + created_at_epoch`). **Importance/confidence/priority: not documented** — no such stored fields; the only ranking signal is query-time (FTS5 rank; file-read-gate "specificity" scoring). **Temporal model**: dual timestamps everywhere (ISO TEXT + epoch INTEGER); server runtime events carry `occurredAtEpoch` (unix millis).

## 3. Metadata & organization

- **Tags/keywords/entities**: no entity model. `facts` and `concepts` are untyped TEXT columns extracted by AI (encoding not documented); `concepts` is filterable (e.g. `build_corpus(concepts: "comma-separated concepts")`). No user tags.
- **Scoping**: by `project` = working-directory basename, a plain TEXT column on every table. Newer runtime adds `platformSource` (`claude, codex, cursor` — "restricts results to that agent's own memory") and project chains: `projects: "Array or comma-separated string; last project is treated as primary."` Team-tier "per-project scopes, roles/access control" is "coming soon" (cloud).
- **Relationships**: grouping is purely by session ids (three of them: `session_id`, `sdk_session_id`, `claude_session_id`) + `prompt_number` + `correlation_id`. No links between memories; temporal adjacency via the `timeline` tool substitutes for explicit threading.

## 4. Lifecycle

Append-only in practice. **Create**: automatic (hooks → worker → AI compression) or manual (`observation_add`, server runtime only). **Update: not documented** (no update tool or endpoint). **Delete/forget: not documented anywhere** — notably `DELETE /sessions/:sessionDbId` is documented as sessions "marked complete instead of deleted"; the only exclusion mechanism is preventive: `<private>` tags stripped before storage. **Decay/expiry: not documented.** **Consolidation**: yes — the Stop hook triggers per-session AI summarization into `session_summaries` (multiple per session, per prompt_number); "relevant summaries from the last 10 sessions automatically injected at startup." No re-consolidation of old observations. Versioning: none; import dedupes via composite keys ("Skips duplicates automatically").

## 5. API surface

**Ingestion is automatic via hooks** (setup `version-check.js` + 5 lifecycle hooks): SessionStart (`context-hook.js` — injects context), UserPromptSubmit (`new-hook.js` — `INSERT OR IGNORE` session, save prompt), PostToolUse (`save-hook.js` — fires on every tool except a skip-list of TodoWrite/Skill/SlashCommand/etc., HTTP POST to worker with 2s timeout, fire-and-forget; "AI compression happens asynchronously in the worker without blocking Claude's tool execution"), Stop (`summary-hook.js` — sends transcript tail for summarization), SessionEnd (`cleanup-hook.js` — mark completed). Plus a PreToolUse Read-matcher (file-read gate).

**MCP read tools** (verbatim from the installed plugin's schemas): `search(query, limit, project, platformSource, type, obs_type, dateStart, dateEnd, offset, orderBy)` → compact index; `timeline(anchor | query, depth_before=3, depth_after=3, project)`; `get_observations(ids: number[] required)` → full details; and a no-op `__IMPORTANT` tool whose description IS the workflow doc: "1. search(query) → Get index with IDs… 3. get_observations([IDs]) → Fetch full details ONLY for filtered IDs. NEVER fetch full details without filtering first."

**MCP write/server-runtime tools** (v13.x, Postgres-backed): `observation_add(content required, projectId, serverSessionId, kind, metadata)` — "Calls /v1/memories — does NOT enqueue generation"; `observation_record_event(eventType required e.g. PostToolUse, payload, sourceType: hook|worker|provider|server|api, occurredAtEpoch, generate=true)` — "server inserts the event row, the outbox row, and enqueues a generation job atomically"; `observation_generation_status(jobId)`; `observation_search(query, projectId, platformSource, limit)`; `observation_context(query)` — "returns matched observations AND a pre-joined context string suitable for prompt injection"; `session_start_context(project|projects, platformSource, full, colors)`; plus legacy aliases `memory_add/memory_search/memory_context`. **Knowledge agents**: `build_corpus(name, types, concepts, files, query, dateStart/End, limit=500)`, `prime_corpus`, `query_corpus(name, question)`, `list_corpora`, `rebuild_corpus`, `reprime_corpus`. Adjacent code tools: `smart_search/smart_outline/smart_unfold` (tree-sitter). **Worker HTTP API**: 23 REST endpoints (`/api/search`, `/api/observations`, `/api/observations/batch`, `/api/context/inject`, `/sessions/:id/init|observations|summarize`, SSE `/stream`, queue management, etc.) on port `37700 + (uid % 100)`. Also a `mem-search` skill (progressive-disclosure instructions, ~250-token frontmatter) and export/import CLI.

## 6. Search/recall

Three retrieval modes: **FTS5 full-text** (primary; virtual tables `observations_fts` over title/subtitle/narrative/text/facts/concepts, `session_summaries_fts`, `user_prompts_fts`, trigger-synced, quote-escaped for injection safety), **semantic via Chroma** (optional), and **hybrid** ("full-text ranking fused with the freshest observations"). Server runtime "Phase 1" uses a Postgres "GIN tsvector index" instead. Pipeline is the 3-layer progressive disclosure: Layer 1 `search` → `SELECT * FROM observations_fts WHERE observations_fts MATCH ? AND type = ? ORDER BY rank LIMIT ? OFFSET ?` returning an index table with per-row retrieval cost (`| ID | Time | T | Title | Tokens |` … `| #2543 | 2:14 PM | 🔴 | Hook timeout: 60s too short… | ~155 |`); Layer 2 `timeline` (chronological neighborhood around an anchor); Layer 3 `get_observations` (500–1,000 tokens each). Ranking: FTS5 `rank`, plus `orderBy` date sorts; no learned reranker documented. **Injection paths**: (a) SessionStart hook calls `GET /api/context/inject?project=X`, worker runs `SELECT * FROM observations WHERE project=X ORDER BY created_at DESC LIMIT 50`, returned as markdown in `hookSpecificOutput.additionalContext`; (b) file-read gate injects a ranked observation timeline instead of file contents; (c) agent-initiated MCP search; (d) server runtime's `observation_context` pre-joined context string.

## 7. Enrichment & classification

All enrichment is **background/worker-side LLM work**, not agent-side — default model `claude-haiku-4-5-20251001` via `@anthropic-ai/claude-agent-sdk` (configurable to Sonnet/Opus, Gemini, OpenRouter, LiteLLM, custom Anthropic-compatible backends). Capabilities with evidence:
- **Observation compression/extraction**: raw tool input/output → `title, subtitle, narrative, facts, concepts, type` via "XML-structured prompts" (`src/sdk/prompts.ts`) and XML response parsing (`src/sdk/parser.ts`); "10:1 to 100:1" compression ratios claimed. One long-running SDK session (v4 lesson: not one per observation).
- **Type classification**: into the 6-value enum (decision/bugfix/feature/refactor/discovery/change).
- **Session summarization**: Stop hook → structured `request/investigated/learned/completed/next_steps` summary.
- **Knowledge-agent priming**: `prime_corpus` "creates an AI session loaded with the corpus knowledge" for Q&A.
- **Not documented**: entity extraction, importance scoring, embedding pipeline details (Chroma sync mechanism explicitly undocumented), prompt templates.

## 8. Provenance

Strong. Every observation records `tool_name`, `correlation_id`, `prompt_number`, three session ids (`session_id`, `sdk_session_id`, `claude_session_id`), `project`, `files_read`/`files_modified`, and dual timestamps; raw prompts are kept verbatim in `user_prompts`; the Stop hook consumes the transcript JSONL path; server-runtime events carry `sourceType (hook|worker|provider|server|api)` and `platformSource`. What's absent: no pointer from a summary/observation back to specific transcript offsets or raw tool payloads (raw output is discarded after compression), and no model/prompt-version stamp on generated fields.

## 9. Verdict

**Warts (evidenced):**
1. **No forget primitive.** Deletion, decay, expiry, and update are undocumented across every page checked; even `DELETE /sessions/:id` only marks complete. A memory system with no governed forgetting is a liability (privacy is preventive-only via `<private>` tags).
2. **Identity is local autoincrement.** Integer PKs minted by SQLite force export/import to dedupe on fragile composite keys (`sdk_session_id + title + created_at_epoch`) and make ids non-portable across machines — exactly what their own cloud sync needs. Mint globally unique ids.
3. **Session-id scar tissue.** Admitted "Session ID Chaos" ("Turn 1: session_abc123 / Turn 2: session_def456") left three session-id columns on one table.
4. **Tool sprawl and duplicate surfaces.** Dec 2025 they deleted 9+ overlapping MCP tools (88% code reduction, 2,718→312 lines) yet the current plugin re-grew a dual surface: `memory_*` legacy aliases beside `observation_*`, and two runtimes (SQLite worker vs Postgres server) with divergent semantics.
5. **Load-everything-first, refactor later.** v1–v3 injected all observations upfront ("Even compressed observations can pollute context if loaded all"); plus fire-and-forget hooks with 2s timeouts lost data under aggressive worker shutdown ("Pending observations lost").

**Worth stealing:**
1. **Progressive disclosure with priced retrieval** — index rows carry token cost so the agent makes "informed ROI decisions"; claimed ~10x savings; workflow enforced via an always-visible `__IMPORTANT` tool description.
2. **Async enrichment off the hot path** — hooks never block the agent; a cheap model compresses in a background worker; the atomic event+outbox+job pattern in `/v1/events` maps directly onto an event-sourced KurrentDB design.
3. **Structured observation typology + structured session summaries** (`request/investigated/learned/completed/next_steps`) — far more queryable than blob memories; three FTS-indexed record kinds (observations/summaries/prompts) searchable uniformly with `obs_type` filter.
4. **Timeline as a first-class retrieval primitive** — anchor + depth_before/depth_after temporal neighborhood; pairs naturally with an event log.
5. **File Read Gate** — PreToolUse interception substituting memory for expensive re-reads (75–97% claimed savings): memory as an active cost-reducer, not just recall.

## 10. Sources

- https://cmem.ai/ ; https://docs.claude-mem.ai/architecture/overview ; https://docs.claude-mem.ai/architecture/search-architecture
- https://docs.claude-mem.ai/architecture/database.md ; …/architecture/hooks.md ; …/architecture/worker-service.md ; …/architecture-evolution.md ; …/progressive-disclosure.md ; …/file-read-gate.md ; …/usage/search-tools.md ; …/usage/export-import.md ; …/context-engineering.md ; …/introduction.md ; …/llms.txt
- https://github.com/thedotmack/claude-mem (README, v13.4.0)
- Installed claude-mem plugin MCP tool JSONSchemas (v13.x, exact verbatim signatures — highest-confidence source for section 5)

**Confidence note**: SQL schemas and tool signatures are verbatim (schemas from docs pages; tool schemas from the live plugin). Docs describe the v4–v5 local architecture while the shipped plugin (v13.x) exposes a newer Postgres server runtime — the docs lag the code, so treat v5-era details as historical. Could not verify: exact enrichment prompt templates, Chroma sync internals, encoding of `facts`/`concepts` (JSON vs plain text), any deletion tooling (none found — stated as "not documented" rather than confirmed absent from code), and CMEM Cloud's server-side schema beyond `observations(id, ts, kind)` marketing copy.

---

# Appendix C — Memorizer (agent report, verbatim)

# Memorizer (Petabridge) — Research Report

## 1. Identity & positioning

Memorizer is "a .NET-based service that allows AI agents to store, retrieve, and search through memories using vector embeddings" (README). It is a self-hosted, Docker-distributed (`petabridge/memorizer` on Docker Hub) ASP.NET Core (.NET 10) app on port 5000 exposing (a) an MCP server over Streamable HTTP at `/mcp` and (b) a full MVC web UI at `/`. Storage is PostgreSQL + pgvector via raw Npgsql SQL (no ORM; 22 numbered `.sql` migrations run at startup); Ollama/OpenAI-compatible providers supply embeddings and LLM calls; **Akka.NET is used only for background jobs** (actors: `TitleGenerationActor`, `EmbeddingRegenerationActor`, `DimensionMigrationActor`, `VersionPurgeActor`, `ProgressStream` with SSE progress) — not for the request path. Forked from Dario Griffo's `postg-mem` (the tool namespace is still `PostgMem.Tools`).

## 2. The memory object

`src/Memorizer/Models/Memory.cs`, verbatim (doc comments elided):

```csharp
public class Memory
{
    public MemoryId Id { get; init; }
    public string? TypeLegacy { get; init; }          // 'type_legacy' column, freeform string
    public string Type { get => TypeLegacy ?? string.Empty; init => TypeLegacy = value; }
    public JsonDocument Content { get; init; } = JsonDocument.Parse("{}");
    public string Source { get; init; } = string.Empty;
    public Vector? Embedding { get; init; }           // Nullable during dimension migration
    public Vector? EmbeddingMetadata { get; init; } = null;
    public string[]? Tags { get; init; }
    public Confidence Confidence { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? Title { get; init; }
    public string Text { get; init; } = string.Empty;
    public VersionNumber CurrentVersion { get; init; } = VersionNumber.Initial;
    public SimilarityScore? Similarity { get; set; }
    public List<MemoryRelationship>? Relationships { get; set; }
    public MemoryOwner Owner { get; init; } = MemoryOwner.Unfiled;
    public MemoryTypeEnum MemoryType { get; init; } = MemoryTypeEnum.Reference;
    public ArchetypeEnum Archetype { get; init; } = ArchetypeEnum.Document;
}
```

Notes:
- **Two embeddings per memory**: full (title + body) and metadata-only (title + tags) — the metadata one is what search uses (dual-embedding PoC promoted to production).
- **Identity**: server-minted `Guid.NewGuid()` wrapped in strongly-typed `readonly record struct MemoryId(Guid Value)` (`EntityIds.cs`; also `RelationshipId`, `VersionId`, `EventId`, `WorkspaceId`, `ProjectId`). JSON-serialized as UUID strings.
- **Types (two overlapping systems)**: freeform string `Type` (legacy) plus `MemoryTypeEnum`: `Checklist, WorkLog, Standard, Specification, TodoList, Reference, HowTo, Document, Conversation, Custom`.
- **Archetype** (`ArchetypeEnum`): `Document` (living/editable), `Record` (immutable), `Archived` (soft-deleted), `System` (hidden internal index memories).
- **Confidence**: `Confidence` wraps `UnitInterval`, a `readonly record struct` that throws `ArgumentOutOfRangeException` outside [0.0, 1.0]. Agent-supplied at store time (default 1.0). No separate importance/priority field.
- **Temporal**: `CreatedAt`/`UpdatedAt` only. **No accessed-at or access count anywhere in the model** — recency/usage plays no role in ranking.

## 3. Metadata & organization

- **Tags**: `string[]`, GIN-indexed, AND-logic in `GetByFilter`, soft-boost (not filter) in search. No entities/keywords extraction.
- **Workspaces** (`Workspace.cs`): "Persistent container for organizing memories. Represents products, teams, or areas of focus. Workspaces are rarely closed and support unlimited nesting." Fields: `WorkspaceId Id, WorkspaceId? ParentId, required string Name, required string Slug` (URL-safe, unique within parent), `string? Description, bool IsSystem, JsonDocument? Settings, CreatedAt, UpdatedAt`. Two well-known system workspaces: **Unfiled** (`Guid.Empty`) and **System Memories** (`11111111-1111-…`); system workspaces can't be modified/deleted.
- **Projects**: "Finite work item with lifecycle, completion criteria, and victory conditions." Belong to a workspace, nestable under parent projects. Fields include `ProjectStatusEnum Status` (`draft → active → on_hold → completed/cancelled → archived`), `string? VictoryConditions` ("Markdown description of completion criteria. UI/agent can parse this into a checklist"), `DateTime? CompletedAt`.
- **Memory binding**: polymorphic `readonly record struct MemoryOwner { OwnerTypeEnum Type; Guid Id; }` — every memory is owned by exactly one workspace OR project (`owner_type` + `owner_id` columns); no owner = Unfiled. `projectId`/`workspaceId` are mutually exclusive in Store/Search. Deleting a workspace/project moves its memories to Unfiled (never deletes them).
- **Relationships**: directed edges `MemoryRelationship { RelationshipId Id, MemoryId FromMemoryId, ToMemoryId, string Type, CreatedAt, VersionNumber? CreatedInVersion, ? DeletedInVersion, SimilarityScore? Score, bool TargetArchived }`. Type is freeform at the MCP boundary ('example-of', 'explains', 'related-to') but a `RelationshipTypeEnum` exists: `Parent, Child, Reference, Related, SimilarTo, Cause, Effect, Duplicate, VersionOf, PartOf, Contains, Precedes, Follows, ExampleOf, InstanceOf…`. `similar-to` edges are created **bidirectionally** with the score stored on both rows (ADR 2025-11-28).

## 4. Lifecycle

- **Create** (`Store`): title mandatory (`throw new ArgumentException("A title is required for all new memories.")`); server generates both embeddings synchronously; content sniffed as JSON (extracts `text`/`fact`/`observation`/`content` keys) else stored as plain text.
- **Update**: `Edit` is find-and-replace on `Text` (exact-match, `replace_all` flag); `UpdateMetadata` changes title/type/tags/confidence without re-embedding content. **Every change is versioned**: `MemoryVersion` snapshot (full text + metadata + `RelationshipIds`) plus event-sourced `memory_events` rows — typed events `memory_created, content_updated` (with precomputed `added_lines/removed_lines/change_snippet` diff stats), `metadata_updated` (field-level old/new), `relationship_added/removed`, `memory_reverted`, each with `Timestamp` and `ChangedBy`. `RevertToVersion` restores an old snapshot **as a new version** (no history rewrite) and regenerates embeddings. Versions auto-prune: `MaxVersionsPerMemory = 50` (`VersioningSettings`), plus a `VersionPurgeActor` batch tool.
- **Forget = two levels**: `ArchiveMemory` is soft (sets `Archetype = Archived`; hidden from default search and relationship displays, restorable via `RestoreMemory(restoreAs: document|record)`); `Delete` is hard ("permanently removes the memory including all version history").
- **Consolidation/dedup**: not automatic. `Get` returns "Similar Memories … may be candidates for consolidation" (default on, threshold 0.75) and expects the agent to merge, archive, or link. Old ADRs describe LLM chunking+summarization of large memories, but no chunking code exists in the current `dev` tree — apparently removed.

## 5. API surface

All tools are `[McpServerTool]` methods returning `Task<string>` — **formatted human-readable text, not structured JSON** (StringBuilder output with emoji headers and "💡 Suggestion:" nudges).

**MemoryTools.cs (13 tools)**: `Store(type, text, source, title, tags?, confidence=1.0, relatedTo?, relationshipType?, projectId?, workspaceId?, archetype="document")`; `Edit(id, old_text, new_text, replace_all=false)`; `UpdateMetadata(id, title?, type?, tags?, confidence?)`; `SearchMemories(query, limit=10, minSimilarity=0.7, filterTags?, projectId?, includeUnassigned=false, includeArchived=false, workspaceId?)`; `Get(id, includeVersionHistory=false, versionNumber?, versionLimit=5, includeArchivedRelationships=false, includeSimilar=true, similarityThreshold=0.75, similarLimit=5)`; `Delete(id)`; `GetMany(ids)`; `CreateReference(fromId, toId, type)`; `RevertToVersion(id, versionNumber, changedBy?)`; `ArchiveMemory(id)`; `RestoreMemory(id, restoreAs="document")`; `GetByFilter(tags?, type?, page=1, pageSize=20, projectId?, workspaceId?)`; `ListArchived(page=1, pageSize=20, projectId?)`.

**WorkspaceTools.cs (9 tools)**: `GetWorkspace(workspaceId?, query?, includeSystem=false)` (list/detail/search by args); `CreateWorkspace(name, description?, parentWorkspaceId?)`; `UpdateWorkspace(workspaceId, name?, description?, newParentWorkspaceId?, makeTopLevel=false)`; `DeleteWorkspace(workspaceId)` (refuses if children; memories → Unfiled); `GetProjectContext(projectId?, workspaceId?, query?, status="active", includeMemories=true)`; `CreateProject(workspaceId, name, description?, victoryConditions?, parentProjectId?)`; `UpdateProject(projectId, …, newWorkspaceId?)`; `DeleteProject(projectId)`; `MoveMemory(memoryIds[], projectId?, workspaceId?, toUnfiled=false)`.

`docs/commands.md` is a plain table of the same tools (missing `GetByFilter`; also references a `memorizer_store` name that doesn't exist in code). The web UI controllers expose parallel REST endpoints (e.g., `GET /api/memory/{id}/similar`).

## 6. Search/recall

- **Mode**: hybrid — pgvector cosine on the **metadata embedding** (title+tags) fused with PostgreSQL FTS via **Reciprocal Rank Fusion (k=60)** (ADR 2026-02-14). FTS uses a trigger-maintained weighted `tsvector`: weight A = title, B = tags, C = body, with AND-prefix `to_tsquery` (`postgres:* & configuration:*`) and `ts_rank_cd`. Adaptive weights: 1–2 word queries get FTS weight 1.5 vs vector 1.0.
- **Ranking signals**: RRF rank + **10% tag soft-boost** (tags never hard-filter — ADR 2025-05-23: "The system always errs on the side of showing something, not nothing"). **No recency, no access counts, no confidence/importance weighting** in ranking.
- **`minSimilarity` is a no-op**: "accepted but not applied in HybridSearch… retained for API compatibility" (ADR). Before hybrid, zero-result searches auto-retried at threshold −0.1 (that fallback code still runs in `SearchMemories`).
- **Justification is quantitative**: a checked-in eval framework (`SearchEval/synthetic_corpus.json`, `evaluation_queries.json`, `SearchQualityMetrics.cs`) reporting MRR/Recall@5/NDCG@5; hybrid won (MRR 0.557 vs 0.481).
- **Response shape**: lightweight by default (`SearchSettings.ReturnFullContent = false` — "Default is false to optimize context window usage for LLM agents"); results end with explicit instructions to call `Get`/`GetMany`.
- **Entity search reuses memory search**: workspace/project search runs `HybridSearchSystemMemories` over hidden system-archetype index memories.

## 7. Enrichment & classification

Mostly **agent-side**; server-side enrichment is deliberate and narrow:

- **Embedding generation** (server, synchronous at store/edit): two vectors per memory via `EmbeddingService` (Ollama/OpenAI-compatible).
- **LLM title generation** (server, background, on-demand): `TitleGenerationActor` (Akka.NET) batch-generates titles for untitled legacy memories using `LlmService` (OllamaSharp), triggered from the Tools UI with SSE progress; falls back to `"{contentType} - {date}"` on LLM failure. Not automatic per-store (title is required at store time).
- **System index memories** (server, automatic): creating/updating a project or workspace upserts a hidden `System`-archetype memory (`"[Project Index] {name}"`, tags `["system","project-index","project:{id}"]`, `source: "system"`) so entities become semantically searchable.
- **Embedding regeneration / dimension migration** (server, background actors) when the embedding model changes.
- **Similar-memory discovery** (server, on-demand): pgvector query surfaced in `Get` and the UI; persisting `similar-to` edges requires explicit action. ADR explicitly deferred: "DBSCAN cluster discovery, LLM-generated cluster names, background pre-computation, automatic relationship creation."
- **Not done by server**: auto-tagging, importance scoring, summarization, consolidation — all left to the agent (LLM chunking/summarization existed per 2025 ADRs but no chunking code remains in the `dev` tree).

## 8. Provenance

Thin and freeform: `Source` is an arbitrary string ("e.g., 'user', 'system', 'LLM'"), preserved per-version; every `MemoryEvent` has `string? ChangedBy` (again freeform: 'user', 'LLM', 'system'), and `RevertToVersion` takes a `changedBy` parameter. No structured provenance — no session/conversation ID, no link to source events/documents, no client identity capture. (Auth/multi-user attribution: not documented in the files read; a `docs/security.md` exists but was not fetched.)

## 9. Verdict

**Warts:**
1. **Prose-string tool results.** Every tool returns emoji-decorated formatted text ("💡 Suggestion: …") instead of structured content — unparseable, token-heavy, and format drift breaks agents silently.
2. **Dead `minSimilarity` parameter** — documented as "Minimum similarity threshold (0.0 to 1.0)" in the tool schema yet "accepted but not applied" per the hybrid-search ADR; the agent is invited to tune a knob that does nothing.
3. **Duplicate type systems + stale surface.** `TypeLegacy` string vs `MemoryTypeEnum` coexist ("TODO: Add [Obsolete]… Currently 16 usages"); tool descriptions reference nonexistent tools ("Use ListProjects to find available projects"); commands.md omits `GetByFilter`.
4. **Inconsistent/silent ID handling.** Optional IDs are `string?` parsed defensively (because "MCP clients may send empty strings or 'null'") while required ones are `Guid`; worse, `GetByFilter`'s `ParseOptionalGuid` silently maps an *invalid* GUID to null — a malformed `projectId` silently widens the query to everything.
5. **Archiving destroys state**: `Archived` is a value of the same `Archetype` enum as document/record, so the original archetype is lost and `RestoreMemory` must ask `restoreAs` — a boolean flag + enum would have round-tripped.

**Worth stealing:**
1. **Hybrid RRF search validated by a checked-in eval harness** (MRR/NDCG on synthetic + production corpora before switching defaults), weighted tsvector (title>tags>body), tag-as-soft-boost, and metadata-only embeddings.
2. **Workspace/project taxonomy with a polymorphic owner and a well-known Unfiled workspace** — every memory always has exactly one home; container deletion re-files instead of cascading deletes; "persistent domain vs completable work" distinction with `VictoryConditions`.
3. **Event-sourced versioning**: typed change events with precomputed diff stats and `ChangedBy`, snapshot-per-version, revert-as-new-version, and capped retention — maps almost 1:1 onto a KurrentDB stream-per-memory design.
4. **Context-budget discipline**: lightweight search results by default with explicit "call GetMany with these IDs" follow-ups, plus a find-and-replace `Edit` tool (agents already know this shape from Claude Code).
5. **Document/Record archetype** (mutable knowledge vs immutable log) and the "system memories" trick — indexing your own entities as hidden memories so one search pipeline serves everything.

## 10. Sources

- https://github.com/petabridge/memorizer (repo tree via GitHub API, `dev` branch, 274 entries)
- Raw files at `raw.githubusercontent.com/petabridge/memorizer/dev/…`: `README.md`; `docs/commands.md`; `docs/FEATURES.md`; `src/Memorizer/Tools/MemoryTools.cs` (1257 lines, full); `src/Memorizer/Tools/WorkspaceTools.cs` (934 lines, full); `Models/{Memory,MemoryEvent,MemoryOwner,MemoryRelationship,MemoryVersion,Workspace,EntityIds,SimilarMemory}.cs`; `Models/ValueTypes/UnitInterval.cs`; `Models/Enums/{ArchetypeEnum,MemoryTypeEnum,RelationshipTypeEnum}.cs`; `Settings/{SearchSettings,SimilaritySettings,VersioningSettings}.cs`; `Services/Memory.cs` (excerpts: StoreMemory, system-memory upsert); `Services/LlmService.cs`; `Actors/TitleGenerationActor.cs`; ADRs `2026-02-14-hybrid-search-rrf.md`, `2025-05-23-memory-search-ranking.md`, `2025-11-28-memory-similarity-discovery.md`, `2025-01-27-preserve-original-memories-during-chunking.md`.

**Confidence note**: All schema/tool claims are verbatim from the `dev` branch as of 2026-07-03. Not verified: runtime behavior (nothing executed); auth/multi-tenancy (`docs/security.md`, `configuration.md` not fetched — isolation appears to be deployment-level, one instance = one memory space, but that's inference); whether chunking is truly removed (ADRs remain, no chunking source files exist in the current tree); full `IStorage` interface beyond the excerpts read; `SearchEvaluation.cs` and `SeedData` internals.

---

# Appendix D — Hindsight, Vectorize (agent report, verbatim; added 2026-07-03)

# Hindsight (Vectorize) — Agent Memory System Survey

## 1. Identity & positioning

Hindsight is Vectorize's open-source (MIT, "Copyright (c) 2025 Vectorize AI, Inc.") agent-memory platform — "state-of-the-art memory for AI agents" — with an accompanying paper, *"Hindsight is 20/20: Building Agent Memory that Retains, Recalls, and Reflects"* (arXiv:2512.12818), claiming SOTA on LoCoMo (89.61%) and LongMemEval (91.4%). It is a Python/FastAPI server backed by **PostgreSQL + pgvector** (pluggable vector/BM25 extensions: pgvectorscale, vchord, ParadeDB pg_search, pgroonga; an Oracle backend also exists), self-hostable via Docker/Helm/pip or hosted at `ui.hindsight.vectorize.io`. Agents connect via REST (`:8888`), an MCP server mounted at `/mcp/{bank_id}` (plus stdio `hindsight-local-mcp`), or generated SDKs (Python `hindsight-client`, TypeScript, Go, Rust, CLI); intended consumers are agent frameworks (Claude Code, LangGraph, CrewAI, 50+ integrations) and app developers wanting per-user/per-agent "memory banks."

## 2. The memory object

Everything is a row in one table, `memory_units` (verbatim from the initial Alembic migration, `5a366d414dce`):

- `id UUID DEFAULT gen_random_uuid()` (PK — **server mints identity**; bank_id is client-chosen text)
- `bank_id TEXT NOT NULL`, `document_id TEXT NULL` (FK → documents, CASCADE)
- `text TEXT NOT NULL`, `context TEXT NULL`
- `embedding vector(384)` (HNSW by default; index type per extension)
- `search_vector tsvector GENERATED ALWAYS AS (to_tsvector('english', text || ' ' || context)) STORED` (GIN)
- `event_date TIMESTAMPTZ` (later made nullable), `occurred_start`, `occurred_end`, `mentioned_at TIMESTAMPTZ`
- `fact_type TEXT DEFAULT 'world'` — CHECK originally `('world','bank','opinion','observation')`; migration `d9f6a3b4c5e2` renamed `'bank'` → `'experience'`
- `confidence_score FLOAT` — CHECK 0.0–1.0; **required for `opinion`, optional for `observation`, must be NULL otherwise** (`confidence_score_fact_type_check`)
- `access_count INT DEFAULT 0`, `metadata JSONB DEFAULT '{}'`, `created_at`, `updated_at`
- Later migrations add: `tags`, `source_memory_ids UUID[]` + `proof_count` (observations), `consolidated_at TIMESTAMPTZ` (incremental consolidation tracking), `consolidation_failed_at`, `edited_at` (user-curation marker, distinct from `updated_at`), `text_signals`, `chunk_id`

**Four networks** (paper): World 𝒲 ("objective facts about the external world"), Experience ("the agent's own experiences, actions, or recommendations", first-person), Opinion 𝒪 (tuples *(t, c, τ)*, confidence c ∈ [0,1]), Observation 𝒮 ("preference-neutral summaries of entities synthesized from multiple underlying facts"). In the shipped product the surfaced hierarchy is **Mental Model → Observation → World/Experience fact**; `VALID_RECALL_FACT_TYPES = frozenset(["world", "experience", "observation"])` — opinions are schema-supported but not recallable (see §9).

**Temporal model is bi-temporal**: occurrence interval (`occurred_start`/`occurred_end`, paper: τₛ, τₑ = "when fact was true") vs `mentioned_at` (τₘ, "when the agent learned about it"), plus `created_at` ingestion time. No importance/salience field exists; ranking uses `access_count`, recency, and observation `proof_count` instead.

## 3. Metadata & organization

- **Tags**: `tags` array per unit, "for scoped visibility filtering (e.g., ['project:alpha', 'user:123'])". Matching modes: `'any'`, `'all'`, `'any_strict'`, `'all_strict'`, `'exact'`, plus `tag_groups` — a boolean algebra (`{and:[...]}, {or:[...]}, {not:{...}}` over leaves) AND-ed per group. `metadata` is a string→string JSONB map.
- **Entities**: `entities(id uuid, canonical_name, bank_id, metadata jsonb, first_seen, last_seen, mention_count)` with unique `(bank_id, LOWER(canonical_name))`; `unit_entities(unit_id, entity_id)` join; `entity_cooccurrences(entity_id_1, entity_id_2, cooccurrence_count, last_cooccurred)`. Mention→canonical resolution: the paper's ρ(m) = argmax_e [α·sim_str + β·sim_co + γ·sim_temp] is implemented in `entity_resolver.py` with **α=0.5 (SequenceMatcher string ratio), β=0.3 (co-occurring-entity overlap), γ=0.2 (temporal proximity within 7 days), acceptance threshold 0.6**; candidates fetched via pg_trgm similarity ≥ 0.15. Below threshold, a new entity is created.
- **Banks**: isolation unit ("each bank is like a separate 'brain'"). Configurable: `mission` (natural-language identity), `disposition` (skepticism/literalism/empathy, each 1–5), **directives** ("hard rules the agent must follow"), `observations_mission` (controls what consolidation synthesizes), per-bank LLM/retention config overrides, `mcp_enabled_tools` allowlist. Disposition/mission/directives affect only `reflect`, not `recall`.
- **Relationships**: `memory_links(from_unit_id, to_unit_id, link_type, entity_id, weight 0–1)` with CHECK `link_type IN ('temporal','semantic','entity','causes','caused_by','enables','prevents')`. Built at retain time: semantic links when cosine > θₛ; temporal links with exponentially decaying weight (Δt/σₜ); entity links between units sharing a canonical entity; causal links LLM-extracted. Used by the graph retrieval arm (spreading activation / link expansion with decay factor δ and link-type multipliers μ(ℓ)).

## 4. Lifecycle

- **Create**: `retain` → LLM fact extraction ("coarse-grained chunking producing 2–5 comprehensive facts per conversation turn"), entity extraction + resolution, link creation, embedding. Async by default over an `async_operations` table (status pending/processing/completed/failed); sync mode available. A named-strategy system exists (`strategy: 'exact'` = verbatim storage, bypassing extraction). Raw input is preserved as `documents` + `chunks(chunk_id, document_id, bank_id, chunk_index, chunk_text)`.
- **Update**: `PATCH .../memories/{id}` curation — "Edit text, context, temporal range, fact type, entities"; sets `edited_at`.
- **Consolidation engine**: runs automatically after retain/delete/update (disable via `HINDSIGHT_API_ENABLE_AUTO_CONSOLIDATION=false`) or manually (`POST /consolidate`, optionally scoped by `observation_scopes` tag sets). Groups new facts against existing observations; refines rather than overwrites; near-duplicate observations reconciled at cosine ≥ `0.97` (merge or keep, folding evidence together). On contradiction, docs verbatim: "the final observation captures the **full journey** — not just 'User prefers Vue' but the complete evolution of their preference" ("User was previously a React enthusiast … but has now switched to Vue"). Raw facts are always preserved.
- **Freshness trend** (`engine/reflect/observations.py`, computed — not stored — from evidence timestamps): `Trend ∈ {stable, strengthening, weakening, new, stale}`; windows `recent_days=30`, `old_days=90`; ratio of recent/older evidence *density* > 1.5 → strengthening, < 0.5 → weakening; no recent evidence → stale; all recent → new. Reflect "treats the affected observations as stale and verifies them against raw facts."
- **Forgetting**: no automatic decay-based deletion (recency decays only *ranking*, linearly over 365 days). Explicit mechanisms: **invalidation** (soft delete) *moves* the row into a mirror archive table `invalidated_memory_units` ("if a row is in `memory_units` it is live") with `invalidation_reason`, `invalidated_at`, `entity_ids` snapshot, embedding dropped; revertible. Hard deletes: delete document (cascades memories and their observations), clear bank memories (optional type filter), delete bank, clear observations (resets `consolidated_at` for re-derivation).

## 5. API surface

Three core verbs plus a very large management surface (~60 REST endpoints under `/v1/default/...`; full list captured from the API reference: memories retain/recall/list/get/patch/dry-run-extract, reflect, banks CRUD+config+stats+templates, consolidate/recover, observations scopes/history/clear, mental models CRUD+refresh+history, entities list/get/graph, tags, documents CRUD+chunks+export/import+reprocess, files, operations list/get/cancel/retry, webhooks, audit, llm-traces, health/version/metrics).

**Retain** — `POST /v1/default/banks/{bank_id}/memories`; `RetainRequest = { items: MemoryItem[], async: bool (default false), document_tags (deprecated) }`; `MemoryItem` (verbatim from OpenAPI): `content` (required), `timestamp` ("When the content occurred… defaults to now"), `context`, `metadata`, `document_id`, `entities` ("Optional entities to combine with auto-extracted entities"), `tags`, `observation_scopes` (`per_tag | combined | all_combinations | shared` or explicit scopes), `strategy`, `update_mode` (`replace | append`). Returns `{ success, bank_id, items_count, async, operation_id(s), usage }` — note: **no memory IDs** on async retain.

**MCP tools** (31, verbatim from `mcp_tools.py`): `retain, sync_retain, recall, reflect, list_banks, create_bank, list_mental_models, get_mental_model, create_mental_model, update_mental_model, delete_mental_model, refresh_mental_model, clear_mental_model, list_directives, create_directive, delete_directive, list_memories, get_memory, update_memory, invalidate_memory, list_documents, get_document, delete_document, list_operations, get_operation, cancel_operation, list_tags, get_bank, get_bank_stats, update_bank, delete_bank, clear_memories`. MCP `retain(content, context="general", timestamp, tags, metadata, document_id, strategy, update_mode)` → `{status:"accepted", operation_id}`; `sync_retain` → `{status:"completed", memory_ids:[...]}`; `recall(query, max_tokens=4096, budget="high", types, prefer_observations=False, tags, tags_match="any", tag_groups, query_timestamp, min_scores)`; `reflect(query, context, budget="low", max_tokens, response_schema, tags, tags_match, include_based_on=False, include_trace=False)`. Bank scoping is per-endpoint (URL path `/mcp/{bank_id}/` or `X-Bank-Id` header) so tools need no bank parameter. Read-only/destructive `ToolAnnotations` are set per tool; per-bank tool allowlisting filters `tools/list`.

**SDK** (Python): flat helpers `retain, retain_batch, retain_files, recall, reflect, list_memories, create_bank, set_mission, ...` plus namespaced APIs `memory, banks, documents, entities, mental_models, directives, operations, webhooks, files, monitoring`.

## 6. Search/recall

Four parallel channels ("TEMPR"): **semantic** (pgvector cosine, HNSW), **keyword** (BM25/full-text over GIN tsvector; pluggable true-BM25 backends), **graph** (spreading activation from semantic entry points over `memory_links`), **temporal** (hybrid rule-based + seq2seq parser converts "last spring" to a date range matched against occurrence intervals). Per-arm result caps, then **Reciprocal Rank Fusion** (`score(d) = Σ 1/(k + rank(d))`, k=60; arms named `["semantic","bm25","graph","temporal"]`), then cross-encoder reranking (default `cross-encoder/ms-marco-MiniLM-L-6-v2`; without one, synthetic RRF-derived scores spread over [0.1, 1.0]), then multiplicative boosts (verbatim): `final_score = CE_normalized × recency_boost × temporal_boost × proof_count_boost`, each `boost = 1 + α × (signal − 0.5)` with α = 0.2 (recency, `clamp(1 − days_ago/365, 0.1, 1.0)`), 0.2 (temporal proximity), 0.1 (proof count, `clamp(0.5 + ln(proof_count)/10, …)`); max swing +27%/−23%. Finally greedy top-down selection into the `max_tokens` budget (only memory text counts).

Filters: `types` (world/experience/observation; defaults world+experience), `prefer_observations` (drop raw facts a returned observation consolidated), tags/tag_groups, `query_timestamp` anchor, `min_scores` per stage (`semantic`/`keyword` pushed into SQL arms; `reranker`/`final` post-filters), `budget` low/mid/high. Response (`RecallResult`): `results: MemoryFact[]` — each with `id, text, fact_type, entities, context, occurred_start, occurred_end, mentioned_at, document_id, metadata, chunk_id, tags, source_fact_ids, scores {final, reranker, semantic, keyword}` — plus optional `entities` map (EntityState with per-entity observations), `chunks` map (raw chunk text), `source_facts` map, and `trace`.

## 7. Enrichment & classification

All enrichment is server-side LLM work and is **required for the default path** (only the `'exact'` retain strategy bypasses extraction): fact extraction + fact-type classification + entity extraction (retain), observation synthesis/refinement/merging (consolidation, a 102 KB `consolidator.py`), and the agentic **reflect** loop — an LLM agent with internal tools `search_mental_models, search_observations, recall, expand, done` that reads sources in priority order Mental Models → Observations → Raw Facts and produces `text`, `based_on`, optional `structured_output` against a caller JSON schema. Behavioral profile: bank `disposition` traits — skepticism/literalism/empathy on 1–5 scales (paper adds bias strength β ∈ [0,1]) — verbalized into the reflect system message; `mission` and hard `directives` likewise. Model config: `HINDSIGHT_API_LLM_PROVIDER` (openai/anthropic/gemini/groq/ollama/lmstudio/vertexai/deepseek/zai/minimax/atlas), example default `gpt-4o-mini`; **separate per-operation configs** (`RETAIN_/REFLECT_/CONSOLIDATION_` prefixes) and numbered multi-LLM load-balancing; embeddings default local `BAAI/bge-small-en-v1.5` (384-dim; ONNX/TEI/OpenAI options). A `dry-run-extract` endpoint previews extraction "without persistence… A/B test extraction configs." Mental models auto-refresh after consolidation only when actually stale.

## 8. Provenance

Strong, three-layered: (1) observations carry `source_memory_ids uuid[]` (+ a scaled `observation_sources` table) and per-observation `ObservationEvidence` requiring "**Exact quote from the memory supporting the observation**" plus `memory_id`, `relevance`, `timestamp`; recall can return `source_facts` resolved. (2) Every fact carries `document_id` + `chunk_id` back to preserved raw `chunks.chunk_text` — full conversation grounding. (3) `reflect` returns `based_on` "organized by type (world, experience, observation, mental-models, directives)", plus opt-in `tool_trace`/`llm_trace`; observation and mental-model history endpoints preserve change history "with source facts resolved". A memory can answer "based on what" at fact, belief, and answer level.

## 9. Verdict

**Warts / mistakes**
1. **Paper–product drift on the belief network**: the paper's Opinion network (confidence tuples, reinforce/weaken/contradict updates c′ = c ± α) is vestigial — the DB CHECK still mandates `confidence_score` for opinions and `ReflectResult` examples still show `"opinion": []`, but `VALID_RECALL_FACT_TYPES` excludes `opinion` and the reflect agent has no opinion-writing tool. Beliefs migrated into observations/mental models, leaving dead schema.
2. **"Three operations" marketing vs ~60 REST endpoints + 31 MCP tools**; the engine is a 583 KB `memory_engine.py` and 305 KB `http.py` — the conceptual core is small but the operational surface is enormous.
3. **Naming churn recorded in migrations**: fact_type `'bank'` → (partial `'interactions'`) → `'experience'` (`d9f6a3b4c5e2_rename_bank_to_interactions.py` actually renames to `experience`); `personality`→disposition, `background`→mission. Costly identifier instability for an event-sourced consumer.
4. **Async retain returns no memory identity** (only `operation_id`); read-your-writes requires a second tool (`sync_retain`) — two tools for one concept, and MCP code duplicates every tool body twice (`include_bank_id_param` branches; 3,516-line file).
5. **Embedding dimension baked into DDL** (`vector(384)`): model switches require re-dimensioning migrations, and the invalidation archive deliberately drops embeddings to dodge a dimension-mismatch bug (#2209).
6. **Tag-scoped consolidation fragments beliefs**: docs admit a unique per-call tag yields "one near-identical observation per session," patched via the `observation_scopes: "shared"` escape hatch.

**Best features worth stealing**
1. **Bi-temporal fields on every unit** (`occurred_start/occurred_end` vs `mentioned_at`) + temporal retrieval as a first-class parallel channel, not a filter.
2. **Evidence-grounded observations with computed freshness**: `source_memory_ids` + exact quotes + `proof_count`, and trend (`new/strengthening/stable/weakening/stale`) derived from evidence-timestamp density at read time — nothing to keep consistent.
3. **Invalidation-as-archive**: soft-deleted rows *move* to a mirror table so the hot recall path never carries a state predicate; revertible; `edited_at` distinct from `updated_at` cleanly separates human curation from machine churn.
4. **Score transparency**: every recall result returns per-stage scores (`semantic/keyword/reranker/final`) and requests accept per-stage `min_scores` floors; conservative *multiplicative* boosts (±10%/±10%/±5%) keep secondary signals from overpowering relevance.
5. **Contradiction handling that "captures the journey"** — refine into a belief that encodes the evolution ("was previously a React enthusiast… now switched to Vue") while raw facts stay immutable; plus `dry-run-extract` for testing enrichment without writes.
6. **Per-bank MCP endpoints** (bank in URL/header, not a per-call parameter) with per-bank tool allowlists — scoping by connection, not by argument.

## 10. Sources

- https://hindsight.vectorize.io (product/docs site) and https://hindsight.vectorize.io/api-reference (endpoint list; OpenAPI v0.8.4 downloaded from /openapi.json)
- https://arxiv.org/abs/2512.12818 and https://arxiv.org/html/2512.12818v1 (paper: four networks, ρ(m), TEMPR, RRF, disposition Θ, opinion reinforcement, benchmarks)
- https://github.com/vectorize-io/hindsight — shallow-cloned at v0.8.4; primary evidence: `hindsight-api-slim/hindsight_api/alembic/versions/*` (schema), `mcp_tools.py`, `engine/response_models.py`, `engine/entity_resolver.py`, `engine/search/fusion.py`, `engine/reflect/observations.py`, `engine/reflect/tools_schema.py`, `hindsight-docs/docs/developer/{index.mdx,observations.mdx,retrieval.md,mcp-server.md}`, `.env.example`, `LICENSE`
- https://vectorize.io/hindsight, https://vectorize.io/blog/introducing-hindsight-agent-memory-that-works-like-human-memory, https://benchmarks.hindsight.vectorize.io/

**Confidence note**: the earlier internal Postgres observations are confirmed with corrections — `fact_type` also includes `opinion` (and historically `bank`); `memory_links` types are `temporal/semantic/entity/causes/caused_by/enables/prevents` (not a single "caused_by"); `observation_history` exists via history-table migrations (`a7b8c9d0e1f2_split_history_into_own_tables`) and `observation_sources`. Not verified: hosted-cloud-specific behavior (multi-tenant control plane in `hindsight-control-plane/` was not audited), exact reflect prompt text, and the paper's graph-decay constants (δ, μ(ℓ) values are not documented numerically in the fetched material). The `memory_units.tags` column's exact DDL migration was not individually read (column existence confirmed via API models and scope backfill migrations).
