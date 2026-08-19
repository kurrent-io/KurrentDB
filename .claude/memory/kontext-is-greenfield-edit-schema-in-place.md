---
name: kontext-is-greenfield-edit-schema-in-place
description: "Kontext is pre-release — edit migration bodies in place and reset the store; never propose a new migration, backfill, or compat path"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: e2b89a65-fdbf-45ee-9465-f8459310bb3a
  modified: 2026-08-19T19:22:36.343Z
---

Kontext is under development with no deployed stores. Migration bodies are NOT frozen: to change
a table, edit the existing migration (e.g. `MemoriesInitialSchema`) directly and let the store be
recreated. Never propose an append-only `ALTER TABLE` step, a journal backfill, a column-drop
migration, or any backward-compatibility path for Kontext data.

**Why:** Sérgio has said this three times, with rising irritation. The append-only/frozen-body rule
is real for the migration *engine's* design, but it does not apply to Kontext's own schema while
the project is pre-release. Treating dev-time schema edits as production migrations wastes his time
and adds ceremony he has explicitly rejected.

**How to apply:** When a Kontext schema change comes up, edit the migration body and move on. Only
raise migration/compat concerns if he says the store has shipped, or for engine behaviour that is
not Kontext's own schema. See [[no-unauthorized-scope-cuts]] and [[definition-of-done-includes-followthrough]].
