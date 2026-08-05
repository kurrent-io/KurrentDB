#!/usr/bin/env bash
# Imports a LongMemEval JSON file (oracle or haystack variant) into a normalized DuckDB
# database, so exploration runs as SQL instead of ad-hoc scripts over the raw file.
#
# Usage:
#   ./import-oracle.sh <longmemeval.json> [out.duckdb]
#
# Tables:  instances, sessions, turns, answer_sessions
# Views:   evidence_turns          — turns the dataset marks as answer evidence
#          seed_memories_default   — what LongMemEvalDataSource emits with default options
set -euo pipefail

SRC="${1:?usage: import-oracle.sh <longmemeval.json> [out.duckdb]}"
OUT="${2:-${SRC%.json}.duckdb}"

rm -f "$OUT"

duckdb "$OUT" <<SQL
CREATE TABLE raw AS
SELECT * FROM read_json('$SRC', format = 'array', sample_size = -1);

CREATE TABLE instances AS
SELECT question_id,
       question_type,
       question,
       answer,
       question_date,
       question_id LIKE '%_abs' AS is_abstention
FROM raw;

CREATE TABLE sessions AS
WITH indexed AS (
    SELECT question_id,
           unnest(generate_series(1, len(haystack_sessions))) AS i,
           haystack_session_ids,
           haystack_dates
    FROM raw
)
SELECT question_id,
       i - 1                   AS session_index,
       haystack_session_ids[i] AS session_id,
       haystack_dates[i]       AS session_date_raw,
       -- "2023/04/10 (Mon) 14:47" — strip the parenthesized day name, parse the rest
       strptime(trim(regexp_replace(haystack_dates[i], '\s*\([^)]*\)\s*', ' ')), '%Y/%m/%d %H:%M') AS session_at
FROM indexed;

CREATE TABLE turns AS
WITH sess AS (
    SELECT question_id,
           unnest(generate_series(1, len(haystack_sessions))) AS si,
           haystack_sessions
    FROM raw
), turn AS (
    SELECT question_id,
           si,
           unnest(generate_series(1, len(haystack_sessions[si]))) AS ti,
           haystack_sessions[si] AS session_turns
    FROM sess
)
SELECT question_id,
       si - 1                  AS session_index,
       ti - 1                  AS turn_index,
       session_turns[ti].role  AS role,
       session_turns[ti].content AS content,
       coalesce(session_turns[ti].has_answer, false) AS has_answer
FROM turn;

CREATE TABLE answer_sessions AS
SELECT question_id, unnest(answer_session_ids) AS session_id
FROM raw;

DROP TABLE raw;

CREATE VIEW evidence_turns AS
SELECT t.*, i.question_type, s.session_at
FROM turns t
JOIN instances i USING (question_id)
JOIN sessions  s USING (question_id, session_index)
WHERE t.has_answer AND trim(t.content) <> '';

-- Mirrors LongMemEvalDataSource default mapping: user turns, plus knowledge-update
-- evidence turns regardless of role; blank turns skipped.
CREATE VIEW seed_memories_default AS
SELECT t.question_id,
       t.session_index,
       t.turn_index,
       s.session_at AS retained_at,
       t.role,
       t.content,
       (i.question_type = 'knowledge-update' AND t.has_answer) AS is_evidence
FROM turns t
JOIN instances i USING (question_id)
JOIN sessions  s USING (question_id, session_index)
WHERE trim(t.content) <> ''
  AND (t.role = 'user' OR (i.question_type = 'knowledge-update' AND t.has_answer));
SQL

duckdb "$OUT" -c "
SELECT 'instances' AS \"table\", count(*) AS rows FROM instances UNION ALL
SELECT 'sessions',  count(*) FROM sessions  UNION ALL
SELECT 'turns',     count(*) FROM turns     UNION ALL
SELECT 'evidence_turns',        count(*) FROM evidence_turns UNION ALL
SELECT 'seed_memories_default', count(*) FROM seed_memories_default;
"

echo "imported: $OUT"
