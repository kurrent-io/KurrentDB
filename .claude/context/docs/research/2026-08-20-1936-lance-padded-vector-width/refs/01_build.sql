-- Deterministic pseudo-random component in [-1, 1]. hash() is a stable UBIGINT hash,
-- so the whole corpus is reproducible across runs and across both tables.
CREATE OR REPLACE MACRO rnd(a, b, c) AS
    (CAST(hash(a * 1000003 + b * 7919 + c) % 2000001 AS DOUBLE) / 1000000.0 - 1.0);

-- Clustered corpus: 75% cluster centroid + 25% per-row noise, then L2-normalised.
-- Uniform noise alone makes every pair near-equidistant and recall meaningless.
CREATE OR REPLACE TABLE corpus AS
WITH ids AS (
    SELECT i AS id, i % 20 AS cl FROM range(0, 10000) t(i)
),
raw AS (
    SELECT id, cl,
           list_transform(range(0, 384), d -> 0.75 * rnd(cl, d, 0) + 0.25 * rnd(id, d, 1)) AS v
    FROM ids
),
nrm AS (
    SELECT id, cl, v, sqrt(list_sum(list_transform(v, y -> y * y))) AS n FROM raw
)
SELECT id,
       cl,
       CAST(list_transform(v, x -> x / n) AS FLOAT[384]) AS v384,
       CAST(list_concat(list_transform(v, x -> x / n),
                        list_transform(range(0, 384), d -> 0.0)) AS FLOAT[768]) AS v768
FROM nrm;

-- 200 held-out queries drawn from the same generator with a disjoint id space.
CREATE OR REPLACE TABLE queries AS
WITH ids AS (
    SELECT 900000 + i AS id, i % 20 AS cl FROM range(0, 200) t(i)
),
raw AS (
    SELECT id, cl,
           list_transform(range(0, 384), d -> 0.75 * rnd(cl, d, 0) + 0.25 * rnd(id, d, 1)) AS v
    FROM ids
),
nrm AS (
    SELECT id, cl, v, sqrt(list_sum(list_transform(v, y -> y * y))) AS n FROM raw
)
SELECT id,
       cl,
       CAST(list_transform(v, x -> x / n) AS FLOAT[384]) AS v384,
       CAST(list_concat(list_transform(v, x -> x / n),
                        list_transform(range(0, 384), d -> 0.0)) AS FLOAT[768]) AS v768
FROM nrm;

SELECT 'corpus' AS what, count(*) AS rows, len(any_value(v384)) AS d384, len(any_value(v768)) AS d768 FROM corpus
UNION ALL
SELECT 'queries', count(*), len(any_value(v384)), len(any_value(v768)) FROM queries;

-- Sanity: the padded tail must be exactly zero and the head must match element-wise.
SELECT count(*) AS mismatched_rows
FROM corpus
WHERE v768[1:384]::FLOAT[384] != v384
   OR list_sum(list_transform(v768[385:768], x -> abs(x))) != 0.0;
