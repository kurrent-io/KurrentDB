// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Text.Json;
using Google.Protobuf.Collections;
using Kurrent.Quack;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure.Grpc;
using KurrentDB.SchemaRegistry.Planes.Storage;
using static KurrentDB.SchemaRegistry.Data.SchemaQueriesMapping;
using SchemaCompatibilityError = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityError;
using SchemaCompatibilityErrorKind = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind;
using SchemaCompatibilityResult = Kurrent.Surge.Schema.Validation.SchemaCompatibilityResult;

namespace KurrentDB.SchemaRegistry.Data;

public class SchemaQueries(IDuckDBConnectionProvider connectionProvider, ISchemaCompatibilityManager compatibilityManager) {
	IDuckDBConnectionProvider ConnectionProvider { get; } = connectionProvider;
	ISchemaCompatibilityManager CompatibilityManager { get; } = compatibilityManager;

	public GetSchemaResponse GetSchema(GetSchemaRequest query) {
		const string sql =
			"""
			SELECT * FROM schemas
			WHERE schema_name = $schema_name
			""";

		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		return connection.QueryOne(
			sql, record => record is not null
				? new GetSchemaResponse { Schema = MapToSchema(record) }
				: throw RpcExceptions.NotFound("Schema", query.SchemaName),
			new { schema_name = query.SchemaName }
		);
	}

	public LookupSchemaNameResponse LookupSchemaName(LookupSchemaNameRequest query) {
		const string sql =
			"""
			SELECT schema_name FROM schema_versions
			WHERE version_id = $schema_version_id
			""";

		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		return connection.QueryOne(
			sql, record => record is not null
				? new LookupSchemaNameResponse { SchemaName = record.schema_name }
				: throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId),
			new { schema_version_id = query.SchemaVersionId, }
		);
	}

	public GetSchemaVersionResponse GetSchemaVersion(GetSchemaVersionRequest query) {
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

		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		return query.HasVersionNumber
			? connection.QueryOne(
				sqlWithVersionNumber, record => record is not null
					? new GetSchemaVersionResponse { Version = MapToSchemaVersion(record) }
					: throw RpcExceptions.NotFound("Schema", query.SchemaName),
				new { schema_name = query.SchemaName, version_number = query.VersionNumber }
			)
			: connection.QueryOne(
				sqlWithoutVersionNumber, record => record is not null
					? new GetSchemaVersionResponse { Version = MapToSchemaVersion(record) }
					: throw RpcExceptions.NotFound("SchemaVersion", query.VersionNumber.ToString()),
				new { schema_name = query.SchemaName }
			);
	}

	public GetSchemaVersionByIdResponse GetSchemaVersionById(GetSchemaVersionByIdRequest query) {
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

		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		return connection.QueryOne(
			sql, record => record is not null
				? new GetSchemaVersionByIdResponse { Version = MapToSchemaVersion(record) }
				: throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId),
			new { schema_version_id = query.SchemaVersionId }
		);
	}

	public ListSchemasResponse ListSchemas(ListSchemasRequest query) {
		const string sql =
			"""
			SELECT * FROM schemas
			WHERE ($schema_name_prefix = '' OR schema_name ILIKE $schema_name_prefix)
			  AND ($tags = '' OR json_contains(tags, $tags))
			""";

		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var result = connection
			.QueryMany<Schema>(
				sql, record => MapToSchema(record),
				new {
					schema_name_prefix = query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : "",
					tags = query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags) : ""
				}
			)
			.ToList();

		return new ListSchemasResponse { Schemas = { result } };
	}

	public ListSchemaVersionsResponse ListSchemaVersions(ListSchemaVersionsRequest query) {
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

		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var result = connection
			.QueryMany<SchemaVersion>(
				query.IncludeDefinition ? sqlIncludeDefinition : sqlExcludeDefinition,
				record => MapToSchemaVersion(record),
				new { schema_name = query.SchemaName }
			)
			.ToList();

		if (result.Count == 0)
			throw RpcExceptions.NotFound("Schema", query.SchemaName);

		return new() {
			Versions = { result }
		};
	}

	public ListRegisteredSchemasResponse ListRegisteredSchemas(ListRegisteredSchemasRequest query) {
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

		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var result = connection
			.QueryMany<RegisteredSchema>(
				sql, record => MapToRegisteredSchema(record),
				new {
					schema_version_id = query.SchemaVersionId,
					schema_name_prefix = query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : "",
					tags = query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags) : ""
				}
			)
			.ToList();

		return new() { Schemas = { result } };
	}

	public async Task<CheckSchemaCompatibilityResponse> CheckSchemaCompatibility(CheckSchemaCompatibilityRequest query, CancellationToken cancellationToken) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var info = query.HasSchemaVersionId
			? GetLatestSchemaValidationInfo(connection, Guid.Parse(query.SchemaVersionId))
			: GetLatestSchemaValidationInfo(connection, query.SchemaName);

		if (query.DataFormat != info.DataFormat) {
			var errors = new RepeatedField<SchemaCompatibilityError> {
				new List<SchemaCompatibilityError> {
					new() {
						Kind = SchemaCompatibilityErrorKind.DataFormatMismatch,
						Details = $"Schema format mismatch: {query.DataFormat} != {info.DataFormat}"
					}
				}
			};

			return new() { Failure = new() { Errors = { errors } } };
		}

		var uncheckedSchema = query.Definition.ToStringUtf8();
		var compatibility = (SchemaCompatibilityMode)info.Compatibility;

		SchemaCompatibilityResult result;

		if (compatibility is SchemaCompatibilityMode.Backward or SchemaCompatibilityMode.Forward or SchemaCompatibilityMode.Full) {
			result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, info.SchemaDefinition.ToStringUtf8(), compatibility, cancellationToken);
		} else {
			var infos = query.HasSchemaVersionId
				? GetAllSchemaValidationInfos(connection, Guid.Parse(query.SchemaVersionId))
				: GetAllSchemaValidationInfos(connection, query.SchemaName);

			var referenceSchemas = infos
				.Select(i => i.SchemaDefinition.ToStringUtf8())
				.ToList();

			result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, compatibility, cancellationToken);
		}

		return MapToSchemaCompatibilityResult(result, info.SchemaVersionId);
	}

	static SchemaValidationInfo GetLatestSchemaValidationInfo(DuckDBAdvancedConnection connection, string schemaName) {
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

		return connection.QueryOne(
			sql, record => record is not null
				? MapToSchemaValidationInfo(record)
				: throw RpcExceptions.NotFound("Schema", schemaName),
			new { schema_name = schemaName }
		);
	}

	static SchemaValidationInfo GetLatestSchemaValidationInfo(DuckDBAdvancedConnection connection, Guid schemaVersionId) {
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

		return connection.QueryOne(
			sql, record => record is not null
				? MapToSchemaValidationInfo(record)
				: throw RpcExceptions.NotFound("SchemaVersion", schemaVersionId.ToString()),
			new { schema_version_id = schemaVersionId }
		);
	}

	static List<SchemaValidationInfo> GetAllSchemaValidationInfos(DuckDBAdvancedConnection connection, string schemaName) {
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

		return connection.QueryMany<SchemaValidationInfo>(
			sql,
			record => MapToSchemaValidationInfo(record),
			new { schema_name = schemaName }
		).ToList();
	}

	static List<SchemaValidationInfo> GetAllSchemaValidationInfos(DuckDBAdvancedConnection connection, Guid schemaVersionId) {
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

		return connection.QueryMany<SchemaValidationInfo>(
			sql,
			record => MapToSchemaValidationInfo(record),
			new { schema_version_id = schemaVersionId }
		).ToList();
	}
}
