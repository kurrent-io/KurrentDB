-- The lance table function refuses subqueries, so every query vector is inlined as a
-- literal. This emits one big UNION ALL per (table, mode) into a .sql file.
CREATE OR REPLACE MACRO leg(target, tbl, col, dim, k, extra) AS TABLE
    SELECT 'CREATE OR REPLACE TABLE ' || target || ' AS ' ||
           string_agg(
               'SELECT ' || id::VARCHAR || ' AS qid, id, cl, _distance FROM lance_vector_search(''' ||
               tbl || ''',''' || col || ''', CAST(' ||
               (CASE WHEN dim = 384 THEN v384::VARCHAR ELSE v768::VARCHAR END) ||
               ' AS FLOAT[' || dim::VARCHAR || ']), k := ' || k::VARCHAR || extra || ')',
               ' UNION ALL ')
           || ';'
    FROM queries;

COPY (FROM leg('exact384', 'ldb.main.n384', 'emb', 384, 50, ', use_index := false'))
  TO '/private/tmp/claude-501/-Users-sergio-dev-kurrent-kurrentdb/0f4655af-1e09-404d-b403-a3e45b7f9f50/scratchpad/probe/g_exact384.sql'
  (FORMAT csv, HEADER false, QUOTE '', DELIMITER E'\x01');

COPY (FROM leg('exact768', 'ldb.main.p768', 'emb', 768, 50, ', use_index := false'))
  TO '/private/tmp/claude-501/-Users-sergio-dev-kurrent-kurrentdb/0f4655af-1e09-404d-b403-a3e45b7f9f50/scratchpad/probe/g_exact768.sql'
  (FORMAT csv, HEADER false, QUOTE '', DELIMITER E'\x01');

COPY (FROM leg('ann384', 'ldb.main.n384', 'emb', 384, 10, ', use_index := true, refine_factor := 1'))
  TO '/private/tmp/claude-501/-Users-sergio-dev-kurrent-kurrentdb/0f4655af-1e09-404d-b403-a3e45b7f9f50/scratchpad/probe/g_ann384.sql'
  (FORMAT csv, HEADER false, QUOTE '', DELIMITER E'\x01');

COPY (FROM leg('ann768', 'ldb.main.p768', 'emb', 768, 10, ', use_index := true, refine_factor := 1'))
  TO '/private/tmp/claude-501/-Users-sergio-dev-kurrent-kurrentdb/0f4655af-1e09-404d-b403-a3e45b7f9f50/scratchpad/probe/g_ann768.sql'
  (FORMAT csv, HEADER false, QUOTE '', DELIMITER E'\x01');

SELECT 'generated' AS status;
