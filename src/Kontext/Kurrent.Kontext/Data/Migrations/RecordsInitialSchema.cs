using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

namespace Kurrent.Kontext.Data.Migrations;

public sealed class RecordsInitialSchema : DuckLanceMigrationScript {
    protected override string Generate() {
        var script =
            $"""
            CREATE TABLE IF NOT EXISTS ldb.main.records (
                log_position  BIGINT,
                record_id     BLOB,
                stream        VARCHAR,
                category      VARCHAR,
                schema_name   VARCHAR,
                schema_format VARCHAR,
                schema_id     VARCHAR,
                data          VARCHAR,
                created_at    BIGINT,
                content       VARCHAR,
                -- The record's properties, as a JSON object. Returned, never searched.
                properties    VARCHAR,
                embedding     FLOAT[{KontextIndexConstants.VectorsDimension}]
            );

            CREATE INDEX log_position_idx ON ldb.main.records (log_position) USING BTREE WITH (replace = true);
            CREATE INDEX record_id_idx    ON ldb.main.records (record_id)    USING BTREE WITH (replace = true);
            CREATE INDEX stream_idx       ON ldb.main.records (stream)       USING BTREE WITH (replace = true);
            CREATE INDEX category_idx     ON ldb.main.records (category)     USING BTREE WITH (replace = true);
            CREATE INDEX schema_name_idx  ON ldb.main.records (schema_name)  USING BTREE WITH (replace = true);

            CREATE INDEX data_fts ON ldb.main.records (data) USING INVERTED WITH (
                replace           = true,
                lance_tokenizer   = 'json',
                base_tokenizer    = 'raw',
                stem              = false,
                remove_stop_words = false,
                lower_case        = true,
                ascii_folding     = true,
                max_token_length  = {KontextIndexConstants.MaxTokenLength}
            );

            CREATE INDEX content_fts ON ldb.main.records (content) USING INVERTED WITH (
                replace           = true,
                analyzer          = 'code',
                base_tokenizer    = 'code',
                split_identifiers = true,
                split_on_numerics = true,
                preserve_original = true,
                stem              = false,
                remove_stop_words = false,
                max_token_length  = {KontextIndexConstants.MaxTokenLength}
            );
            """;

        return script;
    }
}