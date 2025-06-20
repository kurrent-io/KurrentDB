// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Dapper;
using DuckDB.NET.Data;
using Google.Protobuf.Collections;
using Humanizer;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure.Grpc;
using Polly;
using static KurrentDB.SchemaRegistry.Data.SchemaQueriesMapping;
using SchemaCompatibilityError = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityError;
using SchemaCompatibilityErrorKind = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind;
using SchemaCompatibilityResult = Kurrent.Surge.Schema.Validation.SchemaCompatibilityResult;

namespace KurrentDB.SchemaRegistry.Data;

public class SchemaQueries(DuckDBConnectionProvider connectionProvider, ISchemaCompatibilityManager compatibilityManager) {
    DuckDBConnectionProvider    ConnectionProvider   { get; } = connectionProvider;
    ISchemaCompatibilityManager CompatibilityManager { get; } = compatibilityManager;

    /// <summary>
    /// Waits until the schema registry has caught up to the specified position represented by a checkpoint value.
    /// This method monitors the `schemas` or `schema_versions` tables to ensure that a checkpoint value
    /// greater than or equal to the specified position exists.
    ///
    /// NOTE: This method is for tests only.
    /// </summary>
    /// <param name="position">
    /// The checkpoint value representing the position to wait for in the schema registry.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that can be used to propagate notification that the operation should be canceled.
    /// </param>
    /// <returns>
    /// A boolean value indicating whether the schema registry has successfully caught up to the specified checkpoint value.
    /// </returns>
    public async Task<bool> WaitUntilCaughtUp(ulong position, CancellationToken cancellationToken) {
		const string sql =
			"""
			SELECT (SELECT EXISTS (FROM schemas WHERE checkpoint >= $checkpoint))
			    OR (SELECT EXISTS (FROM schema_versions WHERE checkpoint >= $checkpoint))
			""";

		var connection = ConnectionProvider.GetConnection();

		var parameters = new { checkpoint = position };

		var foreverRetryOnResult = Policy
			.HandleResult<bool>(exists => !exists)
			.WaitAndRetryForeverAsync(_ => 100.Milliseconds());

		var retryOnException = Policy<bool>
			.Handle<DuckDBException>()
			.WaitAndRetryAsync(5, _ => 100.Milliseconds());

		var retryPolicy = Policy.WrapAsync<bool>(foreverRetryOnResult, retryOnException);

		var exists = await retryPolicy.ExecuteAsync(async () => await connection.QueryFirstOrDefaultAsync<bool>(sql, parameters));

		return exists;
	}

    public async Task<GetSchemaResponse> GetSchema(GetSchemaRequest query, CancellationToken cancellationToken) {
         const string sql =
             """
             SELECT * FROM schemas
             WHERE schema_name = $schema_name
             """;

         var connection = ConnectionProvider.GetConnection();

         return await connection.QueryOneAsync(
	         sql, record => record is not null
		         ? new GetSchemaResponse { Schema = MapToSchema(record) }
		         : throw RpcExceptions.NotFound("Schema", query.SchemaName),
	         new { schema_name = query.SchemaName }
         );
    }

    public async Task<LookupSchemaNameResponse> LookupSchemaName(LookupSchemaNameRequest query, CancellationToken cancellationToken) {
        const string sql =
            """
            SELECT schema_name FROM schema_versions
            WHERE version_id = $schema_version_id
            """;

        var connection = ConnectionProvider.GetConnection();

        return await connection.QueryOneAsync(
            sql, record => record is not null
                ? new LookupSchemaNameResponse { SchemaName = record.schema_name }
                : throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId),
            new { schema_version_id = query.SchemaVersionId, }
        );
    }

    public async Task<GetSchemaVersionResponse> GetSchemaVersion(GetSchemaVersionRequest query, CancellationToken cancellationToken) {
        const string sqlWithVersionNumber =
            """
            SELECT
                  version_id
                , version_number
                , decode(schema_definition) AS schema_definition
                , data_format
                , registered_at
            FROM schema_versions
            WHERE schema_name = $schema_name
              AND version_number = $version_number
            """;

        const string sqlWithoutVersionNumber =
            """
            SELECT
                  version_id
                , version_number
                , decode(schema_definition) AS schema_definition
                , data_format
                , registered_at
            FROM schema_versions
            WHERE schema_name = $schema_name
            ORDER BY version_number DESC
            LIMIT 1;
            """;

        var connection = ConnectionProvider.GetConnection();

        return query.HasVersionNumber
            ? await connection.QueryOneAsync(
                sqlWithVersionNumber, record => record is not null
                    ? new GetSchemaVersionResponse { Version = MapToSchemaVersion(record) }
                    : throw RpcExceptions.NotFound("Schema", query.SchemaName),
                new { schema_name = query.SchemaName, version_number = query.VersionNumber }
            )
            : await connection.QueryOneAsync(
                sqlWithoutVersionNumber, record => record is not null
                    ? new GetSchemaVersionResponse { Version = MapToSchemaVersion(record) }
                    : throw RpcExceptions.NotFound("SchemaVersion", query.VersionNumber.ToString()),
                new { schema_name = query.SchemaName }
            );
    }

    public async Task<GetSchemaVersionByIdResponse> GetSchemaVersionById(GetSchemaVersionByIdRequest query, CancellationToken cancellationToken) {
        const string sql =
            """
            SELECT
                  version_id
                , version_number
                , decode(schema_definition) AS schema_definition
                , data_format
                , registered_at
            FROM schema_versions
            WHERE version_id = $schema_version_id
            """;

        var connection = ConnectionProvider.GetConnection();

        return await connection.QueryOneAsync(
            sql, record => record is not null
                ? new GetSchemaVersionByIdResponse { Version = MapToSchemaVersion(record) }
                : throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId),
            new { schema_version_id = query.SchemaVersionId }
        );
    }

    public async Task<ListSchemasResponse> ListSchemas(ListSchemasRequest query, CancellationToken cancellationToken) {
        const string sql =
            """
            SELECT * FROM schemas
            WHERE ($schema_name_prefix = '' OR schema_name ILIKE $schema_name_prefix)
              AND ($tags = '' OR json_contains(tags, $tags))
            """;

        var connection = ConnectionProvider.GetConnection();

        var result = await connection
            .QueryManyAsync<Schema>(
                sql, record => MapToSchema(record),
                new {
                    schema_name_prefix = query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : "",
                    tags               = query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags) : ""
                }
            )
            .ToListAsync(cancellationToken);

        return new() { Schemas = { result } };

        // static string BuildSql(ListSchemasRequest query) {
        //     var expressions = new List<string>();
        //
        //     if (query.HasSchemaNamePrefix)
        //         expressions.Add("schema_name ILIKE $schema_name_prefix");
        //
        //     if (query.SchemaTags.Count > 0) {
        //         expressions.Add("json_contains(tags, $tags)");
        //     }
        //
        //     var filters = expressions.Count > 0
        //         ? $"WHERE {string.Join(" AND ", expressions)}"
        //         : "";
        //
        //     return $"SELECT * FROM schemas {filters}";
        // }
    }

    public async Task<ListSchemaVersionsResponse> ListSchemaVersions(ListSchemaVersionsRequest query, CancellationToken cancellationToken) {
        const string sqlIncludeDefinition =
            """
            SELECT
                  version_id
                , version_number
                , decode(schema_definition) AS schema_definition
                , data_format
                , registered_at
            FROM schema_versions
            WHERE schema_name = $schema_name
            ORDER BY version_number
            """;

        const string sqlExcludeDefinition =
            """
            SELECT
                  version_id
                , version_number
                , data_format
                , registered_at
            FROM schema_versions
            WHERE schema_name = $schema_name
            ORDER BY version_number
            """;

        var connection = ConnectionProvider.GetConnection();

        var result = await connection
            .QueryManyAsync<SchemaVersion>(
                query.IncludeDefinition ? sqlIncludeDefinition : sqlExcludeDefinition,
                record => MapToSchemaVersion(record),
                new { schema_name = query.SchemaName }
            )
            .ToListAsync(cancellationToken);

        if (result.Count == 0)
            throw RpcExceptions.NotFound("Schema", query.SchemaName);

        return new() {
            Versions = { result }
        };
    }

    public async Task<ListRegisteredSchemasResponse> ListRegisteredSchemas(ListRegisteredSchemasRequest query, CancellationToken cancellationToken) {
        const string sql =
            """
            SELECT
                  s.schema_name
                , s.data_format
                , s.compatibility
                , s.tags
                , v.version_id
                , v.version_number
                , decode(v.schema_definition) AS schema_definition
                , v.registered_at
            FROM schemas s
            INNER JOIN schema_versions v ON s.latest_version_id = v.version_id
            WHERE ($schema_version_id = '' OR v.version_id = $schema_version_id)
              AND ($schema_name_prefix = '' OR s.schema_name ILIKE $schema_name_prefix)
              AND ($tags == '' OR json_contains(s.tags, $tags))
            """;

        var connection = ConnectionProvider.GetConnection();

        var result = await connection
            .QueryManyAsync<RegisteredSchema>(
                sql, record => MapToRegisteredSchema(record),
                new {
                    schema_version_id  = query.SchemaVersionId,
                    schema_name_prefix = query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : "",
                    tags               = query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags) : ""
                }
            )
            .ToListAsync(cancellationToken);

        return new() { Schemas = { result } };
    }

    public async Task<CheckSchemaCompatibilityResponse> CheckSchemaCompatibility(CheckSchemaCompatibilityRequest query, CancellationToken cancellationToken) {
        var info = query.HasSchemaVersionId
            ? await GetLatestSchemaValidationInfo(Guid.Parse(query.SchemaVersionId), cancellationToken)
            : await GetLatestSchemaValidationInfo(query.SchemaName, cancellationToken);

        if (query.DataFormat != info.DataFormat) {
	        var errors = new RepeatedField<SchemaCompatibilityError> {
		        new List<SchemaCompatibilityError> {
			        new() {
				        Kind = SchemaCompatibilityErrorKind.DataFormatMismatch,
				        Details = $"Schema format mismatch: {query.DataFormat} != {info.DataFormat}"
			        }
		        }
	        };

	        return new CheckSchemaCompatibilityResponse {
		        Failure = new CheckSchemaCompatibilityResponse.Types.Failure {
			        Errors = { errors }
		        }
	        };
        }

        var uncheckedSchema = query.Definition.ToStringUtf8();
	    var compatibility = (SchemaCompatibilityMode)info.Compatibility;

	    SchemaCompatibilityResult result;

	    if (compatibility is SchemaCompatibilityMode.Backward or SchemaCompatibilityMode.Forward or SchemaCompatibilityMode.Full) {
		    result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, info.SchemaDefinition.ToStringUtf8(), compatibility, cancellationToken);
	    } else {
		    var infos = query.HasSchemaVersionId
			    ? await GetAllSchemaValidationInfos(Guid.Parse(query.SchemaVersionId), cancellationToken)
			    : await GetAllSchemaValidationInfos(query.SchemaName, cancellationToken);

		    var referenceSchemas = infos
			    .Select(i => i.SchemaDefinition.ToStringUtf8())
			    .ToList();

		    result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, compatibility, cancellationToken);
	    }

	    return MapToSchemaCompatibilityResult(result, info.SchemaVersionId);
    }

    async Task<SchemaValidationInfo> GetLatestSchemaValidationInfo(string schemaName, CancellationToken cancellationToken) {
        const string sql =
            """
            SELECT
                  v.version_id
                , decode(v.schema_definition) AS schema_definition
                , v.data_format
                , s.compatibility
            FROM schemas s
            INNER JOIN schema_versions v ON v.version_id = s.latest_version_id
            WHERE s.schema_name = $schema_name
            """;

        var connection = ConnectionProvider.GetConnection();

        return await connection.QueryOneAsync(
            sql, record => record is not null
                ? MapToSchemaValidationInfo(record)
                : throw RpcExceptions.NotFound("Schema", schemaName),
            new { schema_name = schemaName }
        );
    }

    async Task<SchemaValidationInfo> GetLatestSchemaValidationInfo(Guid schemaVersionId, CancellationToken cancellationToken) {
        const string sql =
            """
            SELECT
                  v.version_id
                , decode(v.schema_definition) AS schema_definition
                , v.data_format
                , s.compatibility
            FROM schemas s
            INNER JOIN schema_versions v ON v.version_id = s.latest_version_id
            WHERE s.schema_name = (
                SELECT schema_name FROM schema_versions
                WHERE version_id = $schema_version_id
            )
            """;

        var connection = ConnectionProvider.GetConnection();

        return await connection.QueryOneAsync(
            sql, record => record is not null
                ? MapToSchemaValidationInfo(record)
                : throw RpcExceptions.NotFound("SchemaVersion", schemaVersionId.ToString()),
            new { schema_version_id = schemaVersionId }
        );
    }

    async Task<List<SchemaValidationInfo>> GetAllSchemaValidationInfos(string schemaName, CancellationToken cancellationToken) {
	    const string sql =
		    """
		    SELECT
		          v.version_id
		        , decode(v.schema_definition) AS schema_definition
		        , v.data_format
		        , s.compatibility
		    FROM schemas s
		    INNER JOIN schema_versions v ON v.schema_name = s.schema_name
		    WHERE s.schema_name = $schema_name
		    ORDER BY v.version_number
		    """;

	    var connection = ConnectionProvider.GetConnection();

	    return await connection.QueryManyAsync<SchemaValidationInfo>(
		    sql,
		    record => MapToSchemaValidationInfo(record),
		    new { schema_name = schemaName }
	    ).ToListAsync(cancellationToken);
    }

    async Task<List<SchemaValidationInfo>> GetAllSchemaValidationInfos(Guid schemaVersionId, CancellationToken cancellationToken) {
	    const string sql =
		    """
		    SELECT
		    	v.version_id
		    	, decode(v.schema_definition) AS schema_definition
		    	, v.data_format
		    	, s.compatibility
		    FROM schemas s
		    INNER JOIN schema_versions v ON v.schema_name = s.schema_name
		    WHERE s.schema_name = (
		    	SELECT schema_name FROM schema_versions
		    	WHERE version_id = $schema_version_id
		    )
		    ORDER BY v.version_number
		    """;

	    var connection = ConnectionProvider.GetConnection();

	    return await connection.QueryManyAsync<SchemaValidationInfo>(
		    sql,
		    record => MapToSchemaValidationInfo(record),
		    new { schema_version_id = schemaVersionId }
	    ).ToListAsync(cancellationToken);
    }
}
