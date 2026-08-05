#!/usr/bin/env bash
# Scores an agent storage run against the sample database: mechanical validity checks plus
# comparison with reference_memories. Input: an NDJSON file in the distilled_memories row
# shape (see haiku-prompt.md). Exit code 0 = no validity violations; 1 = violations found.
#
# Usage: ./eval-storage.sh <run.ndjson> [sample.duckdb]
set -euo pipefail

RUN="${1:?usage: eval-storage.sh <run.ndjson> [sample.duckdb]}"
DIR="$(cd "$(dirname "$0")" && pwd)"
DB="${2:-$DIR/longmemeval_sample.duckdb}"

duckdb <<SQL
ATTACH '$DB' AS sample (READ_ONLY);
USE sample;

CREATE TEMP TABLE run AS
SELECT * FROM read_json('$RUN', format = 'newline_delimited', columns = {
    memory_id: 'VARCHAR', question_id: 'VARCHAR', memory_type: 'VARCHAR', content: 'VARCHAR',
    importance: 'VARCHAR', sentiment: 'VARCHAR', urgency: 'VARCHAR',
    valid_from: 'VARCHAR', valid_to: 'VARCHAR', supersedes: 'VARCHAR[]',
    retained_at: 'VARCHAR', source_session_index: 'INTEGER', source_turn_indexes: 'INTEGER[]'
});

SELECT '== VALIDITY (must all be 0) ==' AS scorecard;
SELECT 'invalid memory_type' AS check, count(*) AS n FROM run
    WHERE memory_type NOT IN ('OBSERVATION','HEARSAY','FACT','USER_PROFILE','PREFERENCE','SUMMARY')
UNION ALL SELECT 'invalid importance/sentiment/urgency', count(*) FROM run
    WHERE importance NOT IN ('LOW','NORMAL','HIGH','CRITICAL')
       OR sentiment NOT IN ('POSITIVE','NEUTRAL','NEGATIVE')
       OR urgency NOT IN ('LOW','MEDIUM','HIGH')
UNION ALL SELECT 'duplicate memory_ids', count(*) FROM (SELECT memory_id FROM run GROUP BY 1 HAVING count(*) > 1)
UNION ALL SELECT 'unknown question_id', count(*) FROM run r
    WHERE NOT EXISTS (SELECT 1 FROM instances i WHERE i.question_id = r.question_id)
UNION ALL SELECT 'orphan supersedes', count(*) FROM (SELECT unnest(supersedes) AS ref FROM run) x
    WHERE NOT EXISTS (SELECT 1 FROM run r WHERE r.memory_id = x.ref)
UNION ALL SELECT 'bad timestamps', count(*) FROM run
    WHERE try_cast(retained_at AS TIMESTAMP) IS NULL
       OR (valid_from IS NOT NULL AND try_cast(valid_from AS TIMESTAMP) IS NULL)
UNION ALL SELECT 'nonexistent source turn', count(*) FROM
    (SELECT question_id, source_session_index AS si, unnest(source_turn_indexes) AS ti FROM run) u
    WHERE NOT EXISTS (SELECT 1 FROM turns t
        WHERE t.question_id = u.question_id AND t.session_index = u.si AND t.turn_index = u.ti);

SELECT '== COVERAGE ==' AS scorecard;
SELECT 'non-abstention instances covered' AS metric,
       count(DISTINCT r.question_id) || ' / ' || (SELECT count(*) FROM instances WHERE NOT is_abstention) AS value
FROM run r JOIN instances i USING (question_id) WHERE NOT i.is_abstention
UNION ALL
SELECT 'evidence turns cited by some memory',
       count(DISTINCT (e.question_id, e.session_index, e.turn_index)) || ' / ' || (SELECT count(*) FROM evidence_turns)
FROM evidence_turns e
JOIN (SELECT question_id, source_session_index AS si, unnest(source_turn_indexes) AS ti FROM run) u
  ON u.question_id = e.question_id AND u.si = e.session_index AND u.ti = e.turn_index
UNION ALL
SELECT 'knowledge-update instances with a supersede pair',
       count(DISTINCT r.question_id) || ' / ' ||
       (SELECT count(DISTINCT i2.question_id) FROM instances i2
        JOIN evidence_turns e2 USING (question_id) WHERE i2.question_type = 'knowledge-update')
FROM run r JOIN instances i USING (question_id)
WHERE i.question_type = 'knowledge-update' AND len(r.supersedes) > 0;

SELECT '== VS REFERENCE ==' AS scorecard;
SELECT 'memories: run vs reference' AS metric,
       (SELECT count(*) FROM run) || ' vs ' || (SELECT count(*) FROM reference_memories) AS value
UNION ALL
SELECT 'type distribution (run)', string_agg(memory_type || ':' || n, ' ' ORDER BY n DESC)
FROM (SELECT memory_type, count(*) AS n FROM run GROUP BY 1)
UNION ALL
SELECT 'type distribution (reference)', string_agg(memory_type || ':' || n, ' ' ORDER BY n DESC)
FROM (SELECT memory_type, count(*) AS n FROM reference_memories GROUP BY 1)
UNION ALL
SELECT 'reference types the run produced',
       (SELECT count(DISTINCT memory_type) FROM run
        WHERE memory_type IN (SELECT memory_type FROM reference_memories)) || ' / ' ||
       (SELECT count(DISTINCT memory_type) FROM reference_memories)
UNION ALL
SELECT 'instances with same type set as reference',
       count(*) || ' / ' || (SELECT count(DISTINCT question_id) FROM reference_memories)
FROM (SELECT question_id, list_sort(list(DISTINCT memory_type)) AS ts FROM run GROUP BY 1) r
JOIN (SELECT question_id, list_sort(list(DISTINCT memory_type)) AS ts FROM reference_memories GROUP BY 1) f
     USING (question_id)
WHERE r.ts = f.ts
UNION ALL
SELECT 'instances exceeding 5 memories (granularity)',
       count(*) || ''
FROM (SELECT question_id FROM run GROUP BY 1 HAVING count(*) > 5);
SQL

VIOLATIONS=$(duckdb -csv -noheader <<SQL
ATTACH '$DB' AS sample (READ_ONLY); USE sample;
CREATE TEMP TABLE run AS SELECT * FROM read_json('$RUN', format = 'newline_delimited', columns = {
    memory_id: 'VARCHAR', question_id: 'VARCHAR', memory_type: 'VARCHAR', content: 'VARCHAR',
    importance: 'VARCHAR', sentiment: 'VARCHAR', urgency: 'VARCHAR',
    valid_from: 'VARCHAR', valid_to: 'VARCHAR', supersedes: 'VARCHAR[]',
    retained_at: 'VARCHAR', source_session_index: 'INTEGER', source_turn_indexes: 'INTEGER[]'});
SELECT count(*) FROM run
WHERE memory_type NOT IN ('OBSERVATION','HEARSAY','FACT','USER_PROFILE','PREFERENCE','SUMMARY')
   OR importance NOT IN ('LOW','NORMAL','HIGH','CRITICAL')
   OR sentiment NOT IN ('POSITIVE','NEUTRAL','NEGATIVE')
   OR urgency NOT IN ('LOW','MEDIUM','HIGH');
SQL
)
[ "$VIOLATIONS" = "0" ]
