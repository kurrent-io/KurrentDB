# LongMemEval Dataset

* https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned

`haiku-prompt.md` holds the worker prompt for re-curating `distilled_memories` with
Haiku-tier agents; its type definitions mirror `McpInstructions.resx`.

## Storage evals (fast path)

* `make-sample.sh` — builds `longmemeval_sample.duckdb`: ~14 curated instances (2 smallest
  per question type + 2 abstentions), same schema, plus `reference_memories` (the curated
  distillation for those instances) as the comparison baseline.
* `eval-storage.sh <run.ndjson>` — scores an agent storage run against the sample:
  validity checks (types, enums, ids, supersedes, source turns), coverage (instances,
  evidence turns, knowledge-update pairs), and type distribution vs the reference.
  Exit 0 = no validity violations.

A run: give agents `haiku-prompt.md` pointed at the sample database, collect NDJSON,
score with `eval-storage.sh`. End to end in minutes. Note: a knowledge-update instance
with a single evidence turn cannot produce a supersede pair — the KU coverage denominator
counts it anyway; read that row with judgment.

## Explore with DuckDB

`import-oracle.sh` normalizes a LongMemEval JSON file into a DuckDB database
(tables: `instances`, `sessions`, `turns`, `answer_sessions`; views:
`evidence_turns`, `seed_memories_default` — the latter mirrors what
`LongMemEvalDataSource` emits with default options).

```sh
./import-oracle.sh path/to/longmemeval_oracle.json          # -> longmemeval_oracle.duckdb
duckdb path/to/longmemeval_oracle.duckdb -c "SELECT count(*) FROM seed_memories_default;"
```

