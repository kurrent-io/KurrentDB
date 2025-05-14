using System.Text.Json;
using Dapper;
using Kurrent.Surge.DuckDB.Projectors;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;

namespace KurrentDB.SchemaRegistry.Data;

public class SchemaProjections : DuckDBProjection {
    public SchemaProjections() {
        Setup(async (db, _) => {
            const string sql =
                """
                CREATE TABLE IF NOT EXISTS schema_versions (
                      version_id        TEXT        PRIMARY KEY
                    , schema_name       TEXT        NOT NULL
                    , version_number    INT         NOT NULL DEFAULT 0
                    , schema_definition BLOB        NOT NULL 
                    , data_format       TINYINT     NOT NULL DEFAULT 0
                    , registered_at     TIMESTAMPTZ NOT NULL DEFAULT current_localtimestamp()
                    , checkpoint        UBIGINT     NOT NULL DEFAULT 0
                );
                
                CREATE INDEX IF NOT EXISTS idx_schema_versions_schema_name ON schema_versions (schema_name);
                CREATE INDEX IF NOT EXISTS idx_schema_versions_version_number ON schema_versions (version_number);
                
                CREATE TABLE IF NOT EXISTS schemas (
                      schema_name           TEXT        PRIMARY KEY
                    , description           TEXT
                    , data_format           TINYINT     NOT NULL DEFAULT 0
                    , latest_version_number INT         NOT NULL DEFAULT 0
                    , latest_version_id     TEXT        NOT NULL
                    , compatibility         TINYINT     NOT NULL
                    , tags                  JSON        NOT NULL DEFAULT '{}'
                    , created_at            TIMESTAMPTZ NOT NULL DEFAULT current_localtimestamp()
                    , updated_at            TIMESTAMPTZ
                    , checkpoint            UBIGINT     NOT NULL DEFAULT 0
                );
                
                CREATE INDEX IF NOT EXISTS idx_schemas_latest_version_id ON schemas (latest_version_id);
                """;

            await db.ExecuteAsync(sql);
        });

        Project<SchemaCreated>(async (msg, db, ctx) => {
            const string sql =
                """
                BEGIN TRANSACTION;

                INSERT INTO schema_versions
                VALUES (
                      $version_id
                    , $schema_name
                    , $version_number
                    , $schema_definition
                    , $data_format
                    , $created_at
                    , $checkpoint
                );

                INSERT INTO schemas
                VALUES (
                      $schema_name
                    , $description
                    , $data_format
                    , $version_number
                    , $version_id
                    , $compatibility
                    , $tags
                    , $created_at
                    , $created_at
                    , $checkpoint
                );

                COMMIT;
                """;

            await db.ExecuteAsync(
                sql,
                new {
                    schema_name       = msg.SchemaName,
                    description       = msg.Description,
                    data_format       = msg.DataFormat,
                    compatibility     = msg.Compatibility,
                    tags              = JsonSerializer.Serialize(msg.Tags),
                    version_id        = msg.SchemaVersionId,
                    version_number    = msg.VersionNumber,
                    schema_definition = msg.SchemaDefinition.ToByteArray(),
                    created_at        = msg.CreatedAt.ToDateTime(),
                    checkpoint        = ctx.Record.LogPosition.CommitPosition
                }
            );
        });

        Project<SchemaVersionRegistered>(async (msg, db, ctx) => {
            const string sql =
                """
                BEGIN TRANSACTION;

                INSERT INTO schema_versions VALUES (
                      $version_id
                    , $schema_name
                    , $version_number
                    , $schema_definition
                    , $data_format
                    , $registered_at
                    , $checkpoint
                );
                
                UPDATE schemas
                SET latest_version_number = $version_number
                  , latest_version_id = $version_id
                  , checkpoint = $checkpoint
                WHERE schema_name = $schema_name;

                COMMIT;
                """;

            await db.ExecuteAsync(
                sql,
                new {
                    version_id        = msg.SchemaVersionId,
                    version_number    = msg.VersionNumber,
                    schema_name       = msg.SchemaName,
                    schema_definition = msg.SchemaDefinition.ToByteArray(),
                    data_format       = msg.DataFormat,
                    registered_at     = msg.RegisteredAt.ToDateTime(),
                    checkpoint        = ctx.Record.LogPosition.CommitPosition
                }
            );
        });

        Project<SchemaCompatibilityModeChanged>(async (msg, db, _) => {
            const string sql =
                """
                UPDATE schemas
                SET compatibility = $compatibility
                  , updated_at = $updated_at
                WHERE schema_name = $schema_name
                """;

            await db.ExecuteAsync(
                sql,
                new {
                    schema_name   = msg.SchemaName,
                    compatibility = msg.Compatibility,
                    updated_at    = msg.ChangedAt.ToDateTime()
                }
            );
        });

        Project<SchemaDescriptionUpdated>(async (msg, db, _) => {
            const string sql =
                """
                UPDATE schemas
                SET description = $description
                  , updated_at = $updated_at
                WHERE schema_name = $schema_name
                """;

            await db.ExecuteAsync(
                sql,
                new {
                    schema_name = msg.SchemaName,
                    description = msg.Description,
                    updated_at  = msg.UpdatedAt.ToDateTime()
                }
            );
        });

        Project<SchemaTagsUpdated>(async (msg, db, _) => {
            const string sql =
                """
                UPDATE schemas
                SET tags = $tags
                  , updated_at = $updated_at
                WHERE schema_name = $schema_name
                """;

            await db.ExecuteAsync(
                sql,
                new {
                    schema_name = msg.SchemaName,
                    tags        = JsonSerializer.Serialize(msg.Tags),
                    updated_at  = msg.UpdatedAt.ToDateTime()
                }
            );
        });

        Project<SchemaVersionsDeleted>(async (msg, db, _) => {
            const string sql =
                """
                BEGIN TRANSACTION;

                DELETE schema_versions
                WHERE json_contains($versions, version_id);

                UPDATE schemas
                SET latest_version_number = $latest_version_number
                  , latest_version_id = $latest_version_id
                WHERE schema_name = $schema_name;

                COMMIT;
                """;

            await db.ExecuteAsync(sql, new {
                versions              = JsonSerializer.Serialize(msg.Versions),
                schema_name           = msg.SchemaName,
                latest_version_id     = msg.LatestSchemaVersionId,
                latest_version_number = msg.LatestSchemaVersionNumber,
            });
        });

        Project<SchemaDeleted>(async (msg, db, _) => {
            const string sql =
                """
                BEGIN TRANSACTION;
                
                DELETE FROM schema_versions
                WHERE schema_name = $schema_name;
                
                DELETE FROM schemas
                WHERE schema_name = $schema_name;
                
                COMMIT;
                """;

            await db.ExecuteAsync(sql, new { schema_name = msg.SchemaName });
        });
    }
}