// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

namespace Kurrent.Kontext.Data.Migrations;

public sealed class MemoriesInitialSchema : DuckLanceMigrationScript {
    protected override string Generate() {
        var script =
            $"""
            CREATE TABLE IF NOT EXISTS ldb.main.memories (
              memory_id        VARCHAR,
              memory_type      INTEGER,
              content          VARCHAR,
              importance       INTEGER,
              tags             VARCHAR[],
              reasoning        VARCHAR,
              evidence         VARCHAR[],
              cited_memory_ids VARCHAR[],
              supersedes       VARCHAR[],
              validity_start   BIGINT,
              validity_end     BIGINT,
              retained_at      BIGINT,
              last_accessed_at BIGINT,
              is_superseded    BOOLEAN,
              superseded_at    BIGINT,
              superseded_by    VARCHAR,
              log_position     BIGINT,
              embedding        FLOAT[{KontextIndexConstants.VectorsDimension}]);

            CREATE INDEX memory_id_idx        ON ldb.main.memories (memory_id)        USING BTREE      WITH (replace = true);
            CREATE INDEX tags_idx             ON ldb.main.memories (tags)             USING LABEL_LIST WITH (replace = true);
            CREATE INDEX evidence_idx         ON ldb.main.memories (evidence)         USING LABEL_LIST WITH (replace = true);
            CREATE INDEX cited_memory_ids_idx ON ldb.main.memories (cited_memory_ids) USING LABEL_LIST WITH (replace = true);
            CREATE INDEX supersedes_idx       ON ldb.main.memories (supersedes)       USING LABEL_LIST WITH (replace = true);
            CREATE INDEX superseded_by_idx    ON ldb.main.memories (superseded_by)    USING BTREE      WITH (replace = true);
            CREATE INDEX log_position_idx     ON ldb.main.memories (log_position)     USING BTREE      WITH (replace = true);
            
            CREATE INDEX content_fts ON ldb.main.memories (content) USING INVERTED WITH (
                replace          = true,
                base_tokenizer   = 'simple',
                language         = 'English',
                stem             = true,
                max_token_length = {KontextIndexConstants.MaxTokenLength}
            );
            """;

        return script;
    }
}