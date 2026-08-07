-- One-time bootstrap: runs once at host start (CreateAsync), never during import ticks.
-- Unqualified names resolve to the engine catalog's main schema — no schema creation needed.
CREATE TABLE IF NOT EXISTS transcripts (
  source                VARCHAR,
  session_id            VARCHAR,
  project_path          VARCHAR,
  project_dir           VARCHAR,
  file_name             VARCHAR,
  is_agent              BOOLEAN,
  line_number           BIGINT,
  message_type          VARCHAR,
  uuid                  VARCHAR,
  parent_uuid           VARCHAR,
  "timestamp"           TIMESTAMPTZ,
  message_role          VARCHAR,
  message_content       VARCHAR,
  model                 VARCHAR,
  tool_name             VARCHAR,
  tool_use_id           VARCHAR,
  tool_input            VARCHAR,
  input_tokens          BIGINT,
  output_tokens         BIGINT,
  cache_creation_tokens BIGINT,
  cache_read_tokens     BIGINT,
  slug                  VARCHAR,
  git_branch            VARCHAR,
  cwd                   VARCHAR,
  version               VARCHAR,
  stop_reason           VARCHAR,
  repository            VARCHAR,
  imported_at           TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE IF NOT EXISTS transcript_parse_errors (
  source       VARCHAR,
  session_id   VARCHAR,
  project_path VARCHAR,
  project_dir  VARCHAR,
  file_name    VARCHAR,
  line_number  BIGINT,
  error        VARCHAR,
  scanned_at   TIMESTAMPTZ
);
