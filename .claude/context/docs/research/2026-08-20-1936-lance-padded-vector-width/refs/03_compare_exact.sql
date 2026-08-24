-- Rank each leg's k=50 hits per query, then compare the two legs position by position.
CREATE OR REPLACE TABLE r384 AS
SELECT qid, id, cl, _distance AS d,
       row_number() OVER (PARTITION BY qid ORDER BY _distance ASC, id ASC) AS rank
FROM exact384;

CREATE OR REPLACE TABLE r768 AS
SELECT qid, id, cl, _distance AS d,
       row_number() OVER (PARTITION BY qid ORDER BY _distance ASC, id ASC) AS rank
FROM exact768;

SELECT 'rows per leg'                                    AS metric,
       (SELECT count(*) FROM r384)::VARCHAR || ' / ' || (SELECT count(*) FROM r768)::VARCHAR AS value
UNION ALL
SELECT 'queries',
       (SELECT count(DISTINCT qid) FROM r384)::VARCHAR
UNION ALL
SELECT 'positions where the id differs',
       (SELECT count(*) FROM r384 a JOIN r768 b USING (qid, rank) WHERE a.id != b.id)::VARCHAR
UNION ALL
SELECT 'positions where the distance differs at all',
       (SELECT count(*) FROM r384 a JOIN r768 b USING (qid, rank) WHERE a.d != b.d)::VARCHAR
UNION ALL
SELECT 'max abs distance delta',
       (SELECT coalesce(max(abs(a.d - b.d)), 0) FROM r384 a JOIN r768 b USING (qid, rank))::VARCHAR
UNION ALL
SELECT 'top-1 agreement',
       (SELECT count(*) FROM r384 a JOIN r768 b USING (qid, rank) WHERE rank = 1 AND a.id = b.id)::VARCHAR
       || ' / ' || (SELECT count(DISTINCT qid) FROM r384)::VARCHAR
UNION ALL
SELECT 'top-10 set agreement (jaccard = 1 count)',
       (SELECT count(*) FROM (
            SELECT qid FROM (SELECT qid, id FROM r384 WHERE rank <= 10)
            EXCEPT ALL
            SELECT qid FROM (SELECT qid, id FROM r768 WHERE rank <= 10)
       ))::VARCHAR || ' mismatched top-10 entries (0 = identical sets)';
