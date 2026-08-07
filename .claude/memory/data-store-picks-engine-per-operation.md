---
name: data-store-picks-engine-per-operation
description: "Sérgio's 'vector store obsession' correction — inside KontextDataStore, use MEVD only for vector-shaped ops; relational reads are plain DuckDB SQL"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: e118a08b-1c86-4d25-97ff-e8db8745c260
  modified: 2026-07-19T22:39:55.169Z
---

Sérgio's correction (2026-07-20, "your vector store obsession"): do not route every operation
through the portable MEVD `VectorStore` abstraction just because the data store wraps one. Inside
[[kontext-kurrentdb-integration-exploration]]'s `KontextDataStore`, the engine surface is chosen
PER OPERATION: vector-shaped ops (semantic search, embedding-generating upserts) use MEVD;
relational ops (listing with any-of filters + ordering, containment lookups, key reads) are plain
DuckDB SQL — one statement with `IN`/`array_has_any`/`ORDER BY`/`LIMIT` beats the portable
surface's no-OR contortions (per-type query multiplication, client-side merge/re-sort, duplicated
sort keys). The old hand-written `Data/KontextStore.cs` is the reference for the SQL shapes.

**Why:** the MEVD filter surface supports only equality + containment with AND — recreating SQL
capabilities on top of it produces slower, more complex code; the whole point of the data-store
seam is that it may query DuckDB directly without leaking that upward.

**How to apply:** when adding or changing a `KontextDataStore` operation, ask "is this operation
vector-shaped?" If not, write it as SQL against DuckDB (once the access door exists — DuckLance's
connection manager is currently internal). Never propose the portable-abstraction version of a
relational query as the end state.
