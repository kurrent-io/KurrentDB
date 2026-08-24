// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

namespace Kurrent.Kontext.Data.Migrations;

public sealed class EntitiesInitialSchema : DuckLanceMigrationScript {
    const int    CleanupIntervalCommits = 1000;
    const string CleanupOlderThan       = "1h";
    const int    CleanupRetainVersions  = 3;

    protected override string Generate() {
        var script =
            $"""
            CREATE TABLE IF NOT EXISTS ldb.main.entities (
              entity_id     VARCHAR,
              entity_type   VARCHAR,
              alias         VARCHAR,
              first_seen_at BIGINT,
              embedding     FLOAT[{KontextIndexConstants.VectorsDimension}]);

            CREATE INDEX entity_id_idx   ON ldb.main.entities (entity_id)   USING BTREE WITH (replace = true);
            CREATE INDEX entity_type_idx ON ldb.main.entities (entity_type) USING BTREE WITH (replace = true);
            CREATE INDEX alias_idx       ON ldb.main.entities (alias)       USING BTREE WITH (replace = true);

            CREATE INDEX alias_fts ON ldb.main.entities (alias) USING INVERTED WITH (
                replace          = true,
                base_tokenizer   = 'simple',
                language         = 'English',
                stem             = true,
                max_token_length = {KontextIndexConstants.MaxTokenLength}
            );

            ALTER TABLE ldb.main.entities SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits},
                older_than      = '{CleanupOlderThan}',
                retain_versions = {CleanupRetainVersions}
            );

            CREATE TABLE IF NOT EXISTS ldb.main.entity_mentions (
              memory_id    VARCHAR,
              span_index   INTEGER,
              span_text    VARCHAR,
              entity_id    VARCHAR,
              confidence   FLOAT,
              resolved_by  INTEGER,
              linked_at    BIGINT);

            CREATE INDEX memory_id_idx ON ldb.main.entity_mentions (memory_id) USING BTREE WITH (replace = true);
            CREATE INDEX entity_id_idx ON ldb.main.entity_mentions (entity_id) USING BTREE WITH (replace = true);

            ALTER TABLE ldb.main.entity_mentions SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits},
                older_than      = '{CleanupOlderThan}',
                retain_versions = {CleanupRetainVersions}
            )
            """;

        return script;
    }
}
