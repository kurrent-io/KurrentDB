---
name: batch-statements-into-one-command
description: "Sérgio's rule — multiple SQL statements/queries go in ONE command/round trip; separate commands only when batching genuinely doesn't work"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: e118a08b-1c86-4d25-97ff-e8db8745c260
  modified: 2026-08-04T01:56:59.071Z
---

Sérgio's rule (2026-07-20, after I ran four introspection SELECTs as four separate commands):
when executing multiple statements, queries, or commands against a database, put them in a SINGLE
command / round trip. "Unless the code does not work, you should always do it."

**Why:** per-command overhead and chatter for zero benefit; the engine and the driver both support
batching (DuckDB.NET runs multi-statement command text; `DbDataReader.NextResult()` walks the
result sets in statement order — validated live on DuckDB 1.5.3 in `DuckDBEngineInfo.From`).
The codebase's own idiom already batches (the Lance pool's init SQL is one multi-statement command).

**How to apply:** default to one command with `;`-separated statements and `NextResult()` for
multiple result sets. Fall back to separate commands only when batching demonstrably fails
(e.g. a statement must observe a prior statement's result in code) — and say so in a comment at
the site. Related: [[data-store-picks-engine-per-operation]].

**PROBED 2026-08-04: named parameters DO ride multi-statement commands** on DuckDB.NET 1.5.3 —
DDL + `$param` insert in one command, and the same parameter referenced by two statements, both
work (scratchpad probe, live engine). The older "parameters don't prepare across a batch"
exception (still quoted in some test-seed comments, e.g. the KontextDataStoreTests seed) no
longer reproduces — do not cite it as a reason to split commands.
