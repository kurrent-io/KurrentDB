---
name: definition-of-done-includes-followthrough
description: "Definition of done includes obvious follow-through (esp. updating docs I maintain) — do it, don't ask permission"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 5e6f16df-d272-4599-b8d8-7d3f46821fc4
---

My "definition of done" for any change must include the obvious follow-through that finishes the job — most importantly, updating any doc I maintain that describes what I changed (project READMEs, `.claude/context` docs) so it stays accurate. Do it in the same turn, automatically.

**Why:** Sérgio is repeatedly and strongly annoyed by questions like "Want me to update the README?" after an obvious change. Keeping docs in sync with the code they describe — and doing the evident next step — is baseline expected behavior, not a decision to hand back to him. Asking permission for the obvious reads as not doing my job.

**How to apply:** When code/design changes and a doc I wrote describes it, update that doc as part of completing the task and report it as done. Only surface a doc edit as a question when there's a genuine decision or a convention constraint (e.g. an immutable point-in-time report — see the knowledge-base rules). This extends the general "act when you have enough info" principle to follow-through, not just the initial ask. Still get approval before touching things explicitly declared off-limits (see [[no-unauthorized-scope-cuts]]).

**Contracts specifically (Sérgio restated this on the Kontext protos, 2026-07-14):** any time I change a contract (`.proto`, public API surface), I MUST update everything it affects in the same turn — sibling comments/doc-comments that reference the changed type, dependent contracts, the `.csproj`/build config (e.g. drop package refs + `Include*Protos` props when the last proto using them is deleted), stale type/message-name references in comments after a rename, and any design/README docs describing the contract. Renames and deletions especially leave dangling references; sweep them, don't leave them.
