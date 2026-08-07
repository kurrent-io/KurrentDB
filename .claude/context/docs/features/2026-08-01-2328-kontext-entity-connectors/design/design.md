---
title: Kontext Entity Connectors
status: exploring        # exploring | settling | superseded
authors: [sergio]
date: 2026-08-01
tags: [kontext, entities, ner, knowledge-graph, connectors, capacitor]
---

# Design Space — Kontext Entity Connectors

> Working doc. Born from the closure of the multi-tenancy session
> (`docs/features/2026-08-01-1753-kurrentdb-multi-tenancy/`, closure banner + decision C1):
> establish what Kontext must build and support so that memories link to real-world entities —
> with **Capacitor as a driver, but not the only one**.

## Problem / Trigger

- Memories mention entities — people, repos, projects, teams, services, tickets. Today those
  mentions are dead text: no identification, no linking, no graph.
- Kontext needs a NER → identification → linking step in its pipeline so that a knowledge
  graph emerges from memories.
- The layering problem: Kontext (platform) must never know application nouns — yet linking
  needs the applications' entities. Capacitor's projects/users/teams live in Capacitor.
- Operator concept: **entity connectors** — a mechanism apps use to hand Kontext their
  vocabulary/mapping, consumed by Kontext in the NER linking step.

Ground rules inherited from the closure (ratified 2026-08-01):

| Rule | Source |
|---|---|
| Kontext owns the MECHANISM; apps supply the VOCABULARY — app nouns enter as data, never as concepts | closure C1 |
| Org dimensions (tenant, workspace, repo-id on memories) are TAGS | Kontext v3 scope-as-tags contract, reconfirmed |
| **Repositories are Kontext's one native entity** — it imports and curates history itself | closure C1 |
| Capacitor may hard-depend on Kontext; its memories feature (AI-1134) converges into Kontext later | closure discussion |
| Multiple driver apps must work — Capacitor first, never alone | operator |
| Ground design reasoning in Park et al. 2023 (Generative Agents); flag deliberate extensions | standing note |

## Exploration

### The shape discussed so far (2026-08-01, chat)

```text
KONTEXT provides the MECHANISM                 APPS provide the VOCABULARY
──────────────────────────────                 ─────────────────────────────
entity registry: (kind, key, name, aliases)    Capacitor registers ITS entities:
  — kind is an opaque app string                 ("capacitor/project", 01K1H0XQ2M, "Checkout", ["checkout"])
NER + linking pipeline over memories             ("capacitor/user", a3f0afb8, "Sérgio", ["ragingkore"])
  — extraction, candidate matching             synced from its own projections
    (aliases + embeddings), link resolution      (e.g. ProjectProjector gains one more sink)
graph storage + query                          repositories: registered by Kontext itself
  memory ─mentions→ entity                       (native entity)
  entity ─cooccurs/cites→ entity
```

- The linking step needs a candidate catalog; the catalog IS what apps register. Kontext links
  mentions to registered entities without understanding any `kind` string — the same stance it
  already takes on tags.
- When Capacitor later swaps its own memories feature for Kontext (AI-1134 convergence), its
  entity registrations are already in place — the graph is waiting for it.

### The connector fork — push vs pull (OPEN, not decided)

The operator's phrase — connectors "**used by Kontext** in the NER linking step" — suggests a
pull model; the chat sketch above was push. Both are real options:

- **Push (registration API):** apps sync entities into Kontext's store
  (`RegisterEntity`/`SyncEntities`). Kontext links against its own local catalog — fast,
  offline, index-friendly (aliases + embeddings precomputed). Cost: sync lag; apps must
  publish changes.
- **Pull (connector interface):** apps expose a connector Kontext CALLS during linking
  (resolve/candidate queries). Always fresh, no duplicate storage. Cost: linking latency bound
  to app availability; RPC inside the pipeline; caching pressure recreates push anyway.
- **Hybrid:** push for the catalog (bulk + updates); optional pull hook only for
  disambiguation of low-confidence links.

### Naming (OPEN)

Operator: "not sure gazetteer is the best name." Working name: **entity connectors**.
Candidates: entity connectors · vocabulary providers · entity catalogs · entity sources.
Settle together with the push/pull fork — the right name depends on which side does the
calling.

## Decisions

- 2026-08-01 — Inherited from the multi-tenancy closure (operator-ratified): the mechanism/
  vocabulary split; org dimensions as tags; repositories native to Kontext; Capacitor a driver
  but not the only one. See the ground-rules table in Problem/Trigger.

## Open Questions

- **Push vs pull vs hybrid** connector model (fork above) — decides the API shape and the name.
- **Naming** — "entity connectors" vs alternatives; settle with the model.
- **Entity identity & lifecycle** — key per `(kind, key)`? renames and alias drift over time?
  merge/split of entities? tombstones when an app deletes one?
- **Pipeline placement** — where NER + linking runs (at retention vs async enrichment pass),
  extractor/embedding models, confidence thresholds, human-curation hooks.
- **Graph model & storage** — mention edges vs entity–entity edges; DuckDB/Lance table shapes;
  how the Lance prefilter constraints bear on entity/mention tables.
- **Scoping on the graph** — do entities and mentions carry the same tag/column scoping as
  memories? cross-workspace entities (a user spans workspaces)?
- **Query surface** — how agents reach the graph: MCP tools, recall enrichment, explicit graph
  queries?
- **Relation to Generative Agents grounding** — mentions/entities as memory objects vs a
  separate substrate; flag whatever is chosen as an extension of Park et al. where it is one.
- **Multi-driver discipline** — `kind` namespacing per app (`capacitor/…`), collision rules,
  and what happens to orphaned vocabularies when an app disappears.
