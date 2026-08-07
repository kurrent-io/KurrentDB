---
name: kontext-v3-contract-state
description: "Settled shape of the Kontext v3 memory gRPC/MCP contracts, and what's deferred — read before resuming \"the Kontext contracts\""
metadata: 
  node_type: memory
  type: project
  originSessionId: fe707916-a2ea-474c-808b-b71115c7af43
  modified: 2026-08-03T16:16:58.305Z
---

Kontext v3 memory contracts live in `src/Kontext/Kurrent.Kontext.Contracts/protos/kurrentdb/kontext/v3/memory/`
(`memory.proto` service+requests, `resources.proto` types, `events.proto` events). `control.proto` was
DELETED — the control plane is out (cross-tenant/admin stuff, incl. InspectRecall/ListRecalls, deferred
to a future ops surface). Builds 0/0. Grounded in Park et al. 2023 (Generative Agents) plus a survey of
Hindsight/Mem0/Zep/Letta/Cognee/OpenViking/Honcho — see [[kontext-ground-in-generative-agents]].

**Settled:**
- `MemoryType` is ONE FLAT enum (combines "what kind" + lifecycle + trust): OBSERVATION, HEARSAY,
  FACT, USER_PROFILE, SUMMARY, PREFERENCE. PROCEDURE (4) and PLAN (6) were RETIRED — a how-to is a
  FACT, a stated intention is HEARSAY — and their numbers stay reserved so old serialized values can
  never alias a new meaning. There is NO `reflection`/`recap` type — reflection is a PROCESS (the
  `reflect` tool); its product is one of these types with `evidence` populated. Trust + lifecycle are
  DERIVED from (type + evidence), not stored axes. Reflection can produce: only SUMMARY (always
  derived); never OBSERVATION/HEARSAY (firsthand); either for FACT/USER_PROFILE/PREFERENCE.
- Boundary guidance baked into the enum: aggregate stats → SUMMARY; personal taste binding only its
  principal → PREFERENCE; a shared standard that binds a team/project → FACT at that scope (a
  preference everyone must follow is a standard); identity/role of a principal → USER_PROFILE.
- 6 data-plane tools, each documented as an MCP tool: `retain` (batch; `reconcile` flag = advisory
  cheap conflict hint, non-blocking), `retract` (NARROW — only wrong-with-no-replacement / forget / when
  you need the cascade; otherwise supersede via `retain { supersedes:[id] }`), `recall` (semantic;
  lean-by-default with `include_full`; returns per-hit `score`), `reclaim` (by id), `recollect` (by
  type/tag, `RecollectSort`+`SortDirection`), `reflect` (async, writes DERIVED memories, emits
  ReflectionCompleted).
- `Evidence` = `reasoning` + `citations` (merged into one message); only derived memories carry it.
- `MemoryImportance` = LOW/NORMAL/HIGH/CRITICAL enum — ADDITIVE salience, NOT a decay control; tunable
  via `ScoringConfig.importance_weights`. (`timeless` was considered and dropped.)
- `metadata` (google.protobuf.Struct) was DELIBERATELY REMOVED from all messages — no escape-hatch bag;
  everything meaningful is typed, anything filterable is a Tag, add a typed field if a real need appears.
- Identity/scope is NOT a field. The principal is AMBIENT from auth (OAuth `sub` on HTTP / trusted
  header / local git user on stdio), server-enforced, never agent-typed. `session`/`project`/`workspace`
  are well-known `Tag` scopes (`Tag.scope` is a free string — the set keeps growing, so tags not fields).
  See [[log-position-not-tfpos]].
- Soul/style are deliberately OUT of Kontext (host-owned, à la soul.md/Hermes — "don't mix the layers").
- `include_retracted` REMOVED from RecollectRequest (settled 2026-07-17, field 3 retired): retracted
  memories were mistakes — there is nothing to audit, so `recollect` never returns them; only
  `reclaim` by exact id still surfaces one. Don't re-suggest the flag.
- Retract CASCADE semantics (settled 2026-07-19): retracting a memory retracts ALL memories derived
  from it — the transitive closure over the CITATION DAG (`Evidence.citations` / the flattened
  `CitedMemoryIds` column). STRICT multi-source rule: a derived memory dies when ANY cited source is
  retracted (unsound premise poisons the inference), even if other sources survive. Cascade follows
  citation edges only, never supersession edges (a superseder is caught only if it itself cites the
  retracted id). Implementation: recursive-CTE closure + one batch retract (single Lance commit =
  atomic + idempotent); rule lives in the service, store gains RetractManyAsync.
- Id collision semantics (settled 2026-07-17): `Memory.memory_id` is OPTIONAL — absent and the server
  mints one. A supplied id must be NEW: `retain` with an id that already exists is EXPLICITLY REJECTED
  (ALREADY_EXISTS, never silently merged), and replacement goes through `supersedes`. `retract` of an
  already-retracted memory stays an idempotent no-op. Enforcement lands in the server core (stateless
  validators can't check existence); documented in memory.proto/resources.proto and McpInstructions.resx.

**Deferred (not built):**
- `Record` verb + `Knowledge`/`Directive`/`MentalModel` standing constructs.
- Shared team/project OWNERSHIP scope + visibility/authz.
- Merging `reclaim` into `recollect` via a SQL-style `filter` string over a virtual schema — DISCUSSED
  and DECLINED for now (would add a server-side filter parser/validator; keep the 6-tool surface).

**Report:** `.claude/context/docs/reports/2026-07-15-memory-type-model-comparison/report.md` (fable/
sonnet/haiku each generated examples of the 7 types → taxonomy is legible across model tiers).
