-- Ground truth is the exact k=50 leg, cut to the top 10.
CREATE OR REPLACE TABLE truth AS
SELECT qid, id
FROM (SELECT qid, id, row_number() OVER (PARTITION BY qid ORDER BY _distance ASC, id ASC) AS rank FROM exact384)
WHERE rank <= 10;

CREATE OR REPLACE MACRO recall_of(leg) AS TABLE
SELECT avg(hits) / 10.0 AS recall_at_10,
       min(hits)        AS worst_query_hits,
       sum(hits)        AS total_hits,
       count(*)         AS queries
FROM (
    SELECT t.qid, count(a.id) AS hits
    FROM truth t
    LEFT JOIN query_table(leg) a ON a.qid = t.qid AND a.id = t.id
    GROUP BY t.qid
);
