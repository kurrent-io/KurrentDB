CREATE TABLE IF NOT EXISTS ldb.main.n384 (id BIGINT, cl BIGINT, emb FLOAT[384]);
CREATE TABLE IF NOT EXISTS ldb.main.p768 (id BIGINT, cl BIGINT, emb FLOAT[768]);

INSERT INTO ldb.main.n384 SELECT id, cl, v384 FROM corpus;
INSERT INTO ldb.main.p768 SELECT id, cl, v768 FROM corpus;

SELECT 'n384' AS tbl, count(*) AS rows FROM ldb.main.n384
UNION ALL
SELECT 'p768', count(*) FROM ldb.main.p768;
