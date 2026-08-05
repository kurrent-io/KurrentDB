#!/usr/bin/env bash
# Builds longmemeval_sample.duckdb: a small curated slice of the oracle database for fast
# storage evals — the 2 smallest instances per question type plus the 2 smallest abstention
# instances (~14 instances), same schema and views as the full database, plus
# reference_memories: the curated distilled_memories rows for those instances, so an eval
# run can score an agent's output against the reference without the full database.
#
# Usage: ./make-sample.sh [source.duckdb] [out.duckdb]
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
SRC="${1:-$DIR/longmemeval_oracle.duckdb}"
OUT="${2:-$DIR/longmemeval_sample.duckdb}"

rm -f "$OUT"

duckdb "$OUT" <<SQL
ATTACH '$SRC' AS src (READ_ONLY);

CREATE TABLE picked AS
WITH sizes AS (
    SELECT i.question_id, i.question_type, i.is_abstention, count(*) AS n_turns
    FROM src.instances i JOIN src.turns t USING (question_id)
    GROUP BY 1, 2, 3
)
SELECT DISTINCT question_id FROM (
    -- 2 smallest instances per question type
    SELECT question_id FROM (
        SELECT question_id, row_number() OVER (PARTITION BY question_type ORDER BY n_turns, question_id) AS r
        FROM sizes WHERE NOT is_abstention) WHERE r <= 2
    UNION ALL
    -- 2 smallest abstention instances
    SELECT question_id FROM (
        SELECT question_id, row_number() OVER (ORDER BY n_turns, question_id) AS r
        FROM sizes WHERE is_abstention) WHERE r <= 2
    UNION ALL
    -- 1 exemplar instance per reference memory type, so every type in the taxonomy is
    -- represented in reference_memories and an agent can be scored on producing it
    SELECT question_id FROM (
        SELECT d.question_id, row_number() OVER (PARTITION BY d.memory_type ORDER BY s.n_turns, d.question_id) AS r
        FROM src.distilled_memories d JOIN sizes s USING (question_id)) WHERE r <= 1
);

CREATE TABLE instances          AS SELECT i.* FROM src.instances i          JOIN picked USING (question_id);
CREATE TABLE sessions           AS SELECT s.* FROM src.sessions s           JOIN picked USING (question_id);
CREATE TABLE turns              AS SELECT t.* FROM src.turns t              JOIN picked USING (question_id);
CREATE TABLE answer_sessions    AS SELECT a.* FROM src.answer_sessions a    JOIN picked USING (question_id);
CREATE TABLE reference_memories AS SELECT d.* FROM src.distilled_memories d JOIN picked USING (question_id);

DROP TABLE picked;

CREATE VIEW evidence_turns AS
SELECT t.*, i.question_type, s.session_at
FROM turns t
JOIN instances i USING (question_id)
JOIN sessions  s USING (question_id, session_index)
WHERE t.has_answer AND trim(t.content) <> '';
SQL

duckdb -readonly "$OUT" -c "
SELECT 'instances' AS t, count(*) AS n FROM instances UNION ALL
SELECT 'turns', count(*) FROM turns UNION ALL
SELECT 'evidence_turns', count(*) FROM evidence_turns UNION ALL
SELECT 'reference_memories', count(*) FROM reference_memories;"
echo "sample: $OUT"
