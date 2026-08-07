---
name: no-unauthorized-scope-cuts
description: "Don't unilaterally defer/cut required functionality; keep it in scope or ask — scoping is the user's call"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 2c7e6446-60e9-4159-8c05-f5661818101c
---

Sérgio pushes back hard when I narrow scope on my own — deferring or dropping required capability and
presenting it as "the plan." In the LanceDB `Microsoft.Extensions.VectorData` connector design I did this
twice: (1) I deferred building the connector itself in favour of just a binding layer, and (2) I marked
**indexing** (vector + scalar index creation, ANN tuners) as "Phase 3, only for speed at scale." Both were
unauthorized scope cuts. Indexing is fundamental — an unindexed vector store is an inferior/regressed
implementation, and MEVD expects collection creation (`EnsureCollectionExistsAsync`) to apply the indexes
declared on the record.

**Why:** Scoping is the user's decision, not mine. "Simplicity First" means don't add *speculative* code —
it does NOT license removing required functionality. Silently deferring a requirement reads as a finished
design while shipping less than asked, and here the target is to *replace* an existing system, so parity
(including indexing) is non-negotiable.

**How to apply:** If something is in scope by the request or by the domain (e.g. indexing for a vector
store, a connector when the ask is a connector), keep it in. If in/out is genuinely ambiguous, use
`AskUserQuestion` — never default to "defer to a later phase." Never present a reduced-capability design
as the plan without explicitly flagging the cut and getting agreement first. Relates to [[log-position-not-tfpos]]
context (same Kontext/Engram vector-memory workstream).
