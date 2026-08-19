---
name: kontext-records-and-memories-are-different-workloads
description: "records and memories are separate index workloads — never assume one tuning, one analyzer or one vector config serves both"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 8ab19cd7-f23b-4bfc-b59b-10d2eca6d770
  modified: 2026-08-18T18:17:41.519Z
---

Sérgio has said this more than once, and I have re-derived shared config for both tables at least
twice. **`ldb.main.records` and `ldb.main.memories` are different workloads. Tune them separately.**

- **memories** — small corpus of natural-language prose written by an agent. FTS is
  `content_fts` on `base_tokenizer='simple', language='English', stem=true`. Benchmarks run at
  ~419 rows.
- **records** — the whole `$all` log, JSON payloads, unbounded growth. FTS is `data_fts`
  (json tokenizer, `field,type,value` triples — the payload IS valid JSON, validated upstream, do
  not re-raise that as a risk) plus `content_fts` (code analyzer, `split_identifiers` and
  `preserve_original` both measured load-bearing).

State as of 2026-08-18: they SHARE one vector config. `EnsureVectorIndex(table, column, options = null)`
does `options ??= new()`, and neither caller passes options — `KontextMaintenanceScheduler` for
memories, `KontextRecordsIndexer` for records. The parameter exists precisely so they can differ;
nothing uses it. No config path reaches `VectorIndexOptions` at all.

**Why:** a measurement on one table does not transfer to the other. Corpus size drives partition
count and whether an index beats brute force; content shape (prose vs JSON) drives the analyzer.
Reporting a records result as if it settles memories is the error to avoid.

**How to apply:** before proposing an index or search change, say which table it is for, and state
explicitly whether it was measured on that table or extrapolated. Related:
[[kontext-lance-fts-tokenizer-contract]], [[kontext-kurrentdb-integration-exploration]].
