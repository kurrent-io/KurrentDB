-- The per-tick import batch: DML only (bootstrap.sql owns all DDL), everything idempotent.
INSTALL agent_data FROM community;
LOAD agent_data;

-- One scan per tick, shared by both tables below: the extension re-reads every JSONL
-- file on each call (no pushdown), so materialize the scan once and split it. OR REPLACE
-- because pooled connections outlive ticks — a failed tick may leave the temp table behind.
CREATE OR REPLACE TEMP TABLE agent_scan AS
SELECT * FROM read_conversations(path := '/Users/sergio/.claude', source := 'claude');

-- Incremental: anti-join on uuid (message identity). Forked/resumed sessions copy
-- prefix messages into new files, so the incoming scan is deduped by uuid too.
INSERT INTO transcripts BY NAME
SELECT
  rc.source, rc.session_id, rc.project_path, rc.project_dir, rc.file_name,
  rc.is_agent, rc.line_number, rc.message_type, rc.uuid, rc.parent_uuid,
  try_cast(rc.timestamp AS TIMESTAMPTZ) AS "timestamp",
  rc.message_role, rc.message_content, rc.model, rc.tool_name, rc.tool_use_id,
  rc.tool_input, rc.input_tokens, rc.output_tokens, rc.cache_creation_tokens,
  rc.cache_read_tokens, rc.slug, rc.git_branch, rc.cwd, rc.version,
  rc.stop_reason, rc.repository
FROM agent_scan AS rc
ANTI JOIN transcripts AS m ON rc.uuid = m.uuid
WHERE rc.message_type <> '_parse_error'
  AND rc.uuid IS NOT NULL
QUALIFY row_number() OVER (PARTITION BY rc.uuid ORDER BY rc.file_name, rc.line_number) = 1;

-- Snapshot refresh, not append: this table always means "lines the CURRENT extension build
-- cannot parse". When a newer build learns a line type, those lines graduate into transcripts
-- on the next tick and drop out of here. Joins to transcripts on session_id.
DELETE FROM transcript_parse_errors;

INSERT INTO transcript_parse_errors
SELECT
  rc.source, rc.session_id, rc.project_path, rc.project_dir, rc.file_name,
  rc.line_number, rc.message_content AS error, now() AS scanned_at
FROM agent_scan AS rc
WHERE rc.message_type = '_parse_error';

DROP TABLE agent_scan;

SELECT
  (SELECT count(*) FROM transcripts)             AS transcripts_total,
  (SELECT count(*) FROM transcript_parse_errors) AS parse_errors_total;
