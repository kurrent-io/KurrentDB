// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Runtime.InteropServices;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Quack;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Infrastructure.Grpc;
using SchemaCompatibilityError = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityError;
using SchemaCompatibilityErrorKind = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind;
using SchemaCompatibilityResult = Kurrent.Surge.Schema.Validation.SchemaCompatibilityResult;

namespace KurrentDB.SchemaRegistry.Data;

public class SchemaQueries(IDuckDBConnectionProvider connectionProvider, ISchemaCompatibilityManager compatibilityManager) {
	IDuckDBConnectionProvider ConnectionProvider { get; } = connectionProvider;
	ISchemaCompatibilityManager CompatibilityManager { get; } = compatibilityManager;

	public GetSchemaResponse GetSchema(GetSchemaRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new SchemaNameArgs(query.SchemaName);
		var schema = connection.QueryFirstOrDefault<SchemaNameArgs, Schema, GetSchemaQuery>(in args).ValueOrDefault
			?? throw RpcExceptions.NotFound("Schema", query.SchemaName);

		return new GetSchemaResponse { Schema = schema };
	}

	public LookupSchemaNameResponse LookupSchemaName(LookupSchemaNameRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new SchemaVersionIdArgs(query.SchemaVersionId);
		var schemaName = connection.QueryFirstOrDefault<SchemaVersionIdArgs, string, LookupSchemaNameQuery>(in args).ValueOrDefault
			?? throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId);

		return new LookupSchemaNameResponse { SchemaName = schemaName };
	}

	public GetSchemaVersionResponse GetSchemaVersion(GetSchemaVersionRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		SchemaVersion? version;

		if (query.HasVersionNumber) {
			var args = new SchemaNameAndVersionNumberArgs(query.SchemaName, query.VersionNumber);
			version = connection.QueryFirstOrDefault<SchemaNameAndVersionNumberArgs, SchemaVersion, GetSchemaVersionByNumberQuery>(in args).ValueOrDefault;
		} else {
			var args = new SchemaNameArgs(query.SchemaName);
			version = connection.QueryFirstOrDefault<SchemaNameArgs, SchemaVersion, GetLatestSchemaVersionQuery>(in args).ValueOrDefault;
		}

		return version is not null
			? new() { Version = version }
			: throw RpcExceptions.NotFound("Schema", query.SchemaName);
	}

	public GetSchemaVersionByIdResponse GetSchemaVersionById(GetSchemaVersionByIdRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new SchemaVersionIdArgs(query.SchemaVersionId);
		var version = connection.QueryFirstOrDefault<SchemaVersionIdArgs, SchemaVersion, GetSchemaVersionByIdQuery>(in args).ValueOrDefault
			?? throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId);

		return new() { Version = version };
	}

	public ListSchemasResponse ListSchemas(ListSchemasRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new SchemaNamePrefixAndTagsArgs(
			query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : "",
			query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags, SchemaRegistryJsonContext.Default.IDictionaryStringString) : "");

		var result = connection
			.ExecuteQuery<SchemaNamePrefixAndTagsArgs, Schema, ListSchemasQuery>(in args)
			.ToList();

		return new ListSchemasResponse { Schemas = { result } };
	}

	public ListSchemaVersionsResponse ListSchemaVersions(ListSchemaVersionsRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new SchemaNameArgs(query.SchemaName);

		var result = query.IncludeDefinition
			? connection.ExecuteQuery<SchemaNameArgs, SchemaVersion, ListSchemaVersionsIncludingDefinitionQuery>(in args).ToList()
			: connection.ExecuteQuery<SchemaNameArgs, SchemaVersion, ListSchemaVersionsExcludingDefinitionQuery>(in args).ToList();

		if (result.Count == 0)
			throw RpcExceptions.NotFound("Schema", query.SchemaName);

		return new() { Versions = { result } };
	}

	public ListRegisteredSchemasResponse ListRegisteredSchemas(ListRegisteredSchemasRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new ListRegisteredSchemasArgs(
			query.SchemaVersionId,
			query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : "",
			query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags, SchemaRegistryJsonContext.Default.IDictionaryStringString) : "");

		var result = connection
			.ExecuteQuery<ListRegisteredSchemasArgs, RegisteredSchema, ListRegisteredSchemasQuery>(in args)
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
		var args = new SchemaNameArgs(schemaName);
		return connection.QueryFirstOrDefault<SchemaNameArgs, SchemaValidationInfo, GetLatestSchemaValidationInfoByNameQuery>(in args).ValueOrDefault
			?? throw RpcExceptions.NotFound("Schema", schemaName);
	}

	static SchemaValidationInfo GetLatestSchemaValidationInfo(DuckDBAdvancedConnection connection, Guid schemaVersionId) {
		var args = new SchemaVersionIdArgs(schemaVersionId.ToString());
		return connection.QueryFirstOrDefault<SchemaVersionIdArgs, SchemaValidationInfo, GetLatestSchemaValidationInfoByVersionIdQuery>(in args).ValueOrDefault
			?? throw RpcExceptions.NotFound("SchemaVersion", schemaVersionId.ToString());
	}

	static List<SchemaValidationInfo> GetAllSchemaValidationInfos(DuckDBAdvancedConnection connection, string schemaName) {
		var args = new SchemaNameArgs(schemaName);
		return connection.ExecuteQuery<SchemaNameArgs, SchemaValidationInfo, GetAllSchemaValidationInfosByNameQuery>(in args).ToList();
	}

	static List<SchemaValidationInfo> GetAllSchemaValidationInfos(DuckDBAdvancedConnection connection, Guid schemaVersionId) {
		var args = new SchemaVersionIdArgs(schemaVersionId.ToString());
		return connection.ExecuteQuery<SchemaVersionIdArgs, SchemaValidationInfo, GetAllSchemaValidationInfosByVersionIdQuery>(in args).ToList();
	}

	static CheckSchemaCompatibilityResponse MapToSchemaCompatibilityResult(SchemaCompatibilityResult result, string schemaVersionId) {
		if (result.Errors.Any())
			return new() { Failure = new() { Errors = { result.Errors.Select(MapToSchemaValidationError) } } };

		return new() { Success = new() { SchemaVersionId = schemaVersionId } };

		static SchemaCompatibilityError MapToSchemaValidationError(Kurrent.Surge.Schema.Validation.SchemaCompatibilityError value) =>
			new() {
				Kind = (SchemaCompatibilityErrorKind)value.Kind,
				Details = value.Details,
				PropertyPath = value.PropertyPath,
				OriginalType = value.OriginalType.ToString(),
				NewType = value.NewType.ToString()
			};
	}

	internal static Timestamp ToTimestamp(DateTime dateTime) =>
		Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));

	internal static MapField<string, string> ParseTags(string json) =>
		string.IsNullOrEmpty(json) || json == "{}"
			? []
			: new() { JsonSerializer.Deserialize(json, SchemaRegistryJsonContext.Default.IDictionaryStringString)! };

	static ByteString ReadSchemaDefinition(ref DataChunk.Row row) =>
		ByteString.CopyFrom(row.ReadBlob().Reference.AsMemory().Span);
}

file readonly record struct SchemaNameArgs(string SchemaName);
file readonly record struct SchemaVersionIdArgs(string SchemaVersionId);
file readonly record struct SchemaNameAndVersionNumberArgs(string SchemaName, int VersionNumber);
file readonly record struct SchemaNamePrefixAndTagsArgs(string SchemaNamePrefix, string Tags);
file readonly record struct ListRegisteredSchemasArgs(string SchemaVersionId, string SchemaNamePrefix, string Tags);

[StructLayout(LayoutKind.Auto)]
file readonly struct GetSchemaQuery : IQuery<SchemaNameArgs, Schema> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT schema_name, description, data_format, compatibility, tags, latest_version_number, created_at, updated_at
		FROM schemas
		WHERE schema_name = $1
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};

	public static Schema Parse(ref DataChunk.Row row) {
		var schemaName = row.ReadString();
		var description = row.ReadString();
		var dataFormat = (SchemaDataFormat)row.ReadSByte();
		var compatibility = (CompatibilityMode)row.ReadSByte();
		var tags = SchemaQueries.ParseTags(row.ReadString());
		var latestVersionNumber = row.ReadInt32();
		var createdAt = row.ReadDateTime();
		var updatedAt = row.TryReadDateTime();

		return new() {
			SchemaName = schemaName,
			Details = new() {
				Description = description,
				DataFormat = dataFormat,
				Compatibility = compatibility,
				Tags = { tags },
			},
			LatestSchemaVersion = latestVersionNumber,
			CreatedAt = SchemaQueries.ToTimestamp(createdAt),
			UpdatedAt = updatedAt is { } dt ? SchemaQueries.ToTimestamp(dt) : null,
		};
	}
}

[StructLayout(LayoutKind.Auto)]
file readonly struct LookupSchemaNameQuery : IQuery<SchemaVersionIdArgs, string> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT schema_name FROM schema_versions
		WHERE version_id = $1
		"""u8;

	public static StatementBindingResult Bind(in SchemaVersionIdArgs args, PreparedStatement source) => new(source) {
		args.SchemaVersionId,
	};

	public static string Parse(ref DataChunk.Row row) => row.ReadString();
}

[StructLayout(LayoutKind.Auto)]
file readonly struct GetSchemaVersionByNumberQuery : IQuery<SchemaNameAndVersionNumberArgs, SchemaVersion> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT version_id, version_number, schema_definition, data_format, registered_at
		FROM schema_versions
		WHERE schema_name = $1
		  AND version_number = $2
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameAndVersionNumberArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
		args.VersionNumber,
	};

	public static SchemaVersion Parse(ref DataChunk.Row row) => SchemaVersionQueries.Parse(ref row, includeDefinition: true);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct GetLatestSchemaVersionQuery : IQuery<SchemaNameArgs, SchemaVersion> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT version_id, version_number, schema_definition, data_format, registered_at
		FROM schema_versions
		WHERE schema_name = $1
		ORDER BY version_number DESC
		LIMIT 1;
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};

	public static SchemaVersion Parse(ref DataChunk.Row row) => SchemaVersionQueries.Parse(ref row, includeDefinition: true);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct GetSchemaVersionByIdQuery : IQuery<SchemaVersionIdArgs, SchemaVersion> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT version_id, version_number, schema_definition, data_format, registered_at
		FROM schema_versions
		WHERE version_id = $1
		"""u8;

	public static StatementBindingResult Bind(in SchemaVersionIdArgs args, PreparedStatement source) => new(source) {
		args.SchemaVersionId,
	};

	public static SchemaVersion Parse(ref DataChunk.Row row) => SchemaVersionQueries.Parse(ref row, includeDefinition: true);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct ListSchemasQuery : IQuery<SchemaNamePrefixAndTagsArgs, Schema> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT schema_name, description, data_format, compatibility, tags, latest_version_number, created_at, updated_at
		FROM schemas
		WHERE ($1 = '' OR schema_name ILIKE $1)
		  AND ($2 = '' OR json_contains(tags, $2))
		"""u8;

	public static StatementBindingResult Bind(in SchemaNamePrefixAndTagsArgs args, PreparedStatement source) => new(source) {
		args.SchemaNamePrefix,
		args.Tags,
	};

	public static Schema Parse(ref DataChunk.Row row) => GetSchemaQuery.Parse(ref row);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct ListSchemaVersionsIncludingDefinitionQuery : IQuery<SchemaNameArgs, SchemaVersion> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT version_id, version_number, schema_definition, data_format, registered_at
		FROM schema_versions
		WHERE schema_name = $1
		ORDER BY version_number
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};

	public static SchemaVersion Parse(ref DataChunk.Row row) => SchemaVersionQueries.Parse(ref row, includeDefinition: true);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct ListSchemaVersionsExcludingDefinitionQuery : IQuery<SchemaNameArgs, SchemaVersion> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT version_id, version_number, data_format, registered_at
		FROM schema_versions
		WHERE schema_name = $1
		ORDER BY version_number
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};

	public static SchemaVersion Parse(ref DataChunk.Row row) => SchemaVersionQueries.Parse(ref row, includeDefinition: false);
}

/// Shared row layout for the two shapes above: with the schema_definition column present or omitted.
file static class SchemaVersionQueries {
	public static SchemaVersion Parse(ref DataChunk.Row row, bool includeDefinition) {
		var versionId = row.ReadString();
		var versionNumber = row.ReadInt32();
		var schemaDefinition = includeDefinition
			? row.ReadBlob().Reference.AsSpan()
			: ReadOnlySpan<byte>.Empty;
		var dataFormat = (SchemaDataFormat)row.ReadSByte();
		var registeredAt = row.ReadDateTime();

		var version = new SchemaVersion {
			SchemaVersionId = versionId,
			VersionNumber = versionNumber,
			DataFormat = dataFormat,
			RegisteredAt = SchemaQueries.ToTimestamp(registeredAt),
			SchemaDefinition = schemaDefinition.IsEmpty ? ByteString.Empty : ByteString.CopyFrom(schemaDefinition),
		};

		return version;
	}
}

[StructLayout(LayoutKind.Auto)]
file readonly struct ListRegisteredSchemasQuery : IQuery<ListRegisteredSchemasArgs, RegisteredSchema> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT
		      s.schema_name
		    , s.data_format
		    , s.compatibility
		    , s.tags
		    , v.version_id
		    , v.version_number
		    , v.schema_definition
		    , v.registered_at
		FROM schemas s
		INNER JOIN schema_versions v ON s.latest_version_id = v.version_id
		WHERE ($1 = '' OR v.version_id = $1)
		  AND ($2 = '' OR s.schema_name ILIKE $2)
		  AND ($3 = '' OR json_contains(s.tags, $3))
		"""u8;

	public static StatementBindingResult Bind(in ListRegisteredSchemasArgs args, PreparedStatement source) => new(source) {
		args.SchemaVersionId,
		args.SchemaNamePrefix,
		args.Tags,
	};

	public static RegisteredSchema Parse(ref DataChunk.Row row) {
		var schemaName = row.ReadString();
		var dataFormat = (SchemaDataFormat)row.ReadSByte();
		var compatibility = (CompatibilityMode)row.ReadSByte();
		var tags = SchemaQueries.ParseTags(row.ReadString());
		var versionId = row.ReadString();
		var versionNumber = row.ReadInt32();
		var schemaDefinition = row.ReadBlob();
		var registeredAt = row.ReadDateTime();

		return new() {
			SchemaName = schemaName,
			DataFormat = dataFormat,
			Compatibility = compatibility,
			Tags = { tags },
			SchemaVersionId = versionId,
			VersionNumber = versionNumber,
			SchemaDefinition = ByteString.CopyFrom(schemaDefinition.Reference.AsMemory().Span),
			RegisteredAt = SchemaQueries.ToTimestamp(registeredAt),
		};
	}
}

[StructLayout(LayoutKind.Auto)]
file readonly struct GetLatestSchemaValidationInfoByNameQuery : IQuery<SchemaNameArgs, SchemaValidationInfo> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT
		      v.version_id
		    , v.schema_definition
		    , v.data_format
		    , s.compatibility
		FROM schemas s
		INNER JOIN schema_versions v ON v.version_id = s.latest_version_id
		WHERE s.schema_name = $1
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};

	public static SchemaValidationInfo Parse(ref DataChunk.Row row) => SchemaValidationInfoQueries.Parse(ref row);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct GetLatestSchemaValidationInfoByVersionIdQuery : IQuery<SchemaVersionIdArgs, SchemaValidationInfo> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT
		      v.version_id
		    , v.schema_definition
		    , v.data_format
		    , s.compatibility
		FROM schemas s
		INNER JOIN schema_versions v ON v.version_id = s.latest_version_id
		WHERE s.schema_name = (
		    SELECT schema_name FROM schema_versions
		    WHERE version_id = $1
		)
		"""u8;

	public static StatementBindingResult Bind(in SchemaVersionIdArgs args, PreparedStatement source) => new(source) {
		args.SchemaVersionId,
	};

	public static SchemaValidationInfo Parse(ref DataChunk.Row row) => SchemaValidationInfoQueries.Parse(ref row);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct GetAllSchemaValidationInfosByNameQuery : IQuery<SchemaNameArgs, SchemaValidationInfo> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT
		      v.version_id
		    , v.schema_definition
		    , v.data_format
		    , s.compatibility
		FROM schemas s
		INNER JOIN schema_versions v ON v.schema_name = s.schema_name
		WHERE s.schema_name = $1
		ORDER BY v.version_number
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};

	public static SchemaValidationInfo Parse(ref DataChunk.Row row) => SchemaValidationInfoQueries.Parse(ref row);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct GetAllSchemaValidationInfosByVersionIdQuery : IQuery<SchemaVersionIdArgs, SchemaValidationInfo> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		SELECT
			v.version_id
			, v.schema_definition
			, v.data_format
			, s.compatibility
		FROM schemas s
		INNER JOIN schema_versions v ON v.schema_name = s.schema_name
		WHERE s.schema_name = (
			SELECT schema_name FROM schema_versions
			WHERE version_id = $1
		)
		ORDER BY v.version_number
		"""u8;

	public static StatementBindingResult Bind(in SchemaVersionIdArgs args, PreparedStatement source) => new(source) {
		args.SchemaVersionId,
	};

	public static SchemaValidationInfo Parse(ref DataChunk.Row row) => SchemaValidationInfoQueries.Parse(ref row);
}

file static class SchemaValidationInfoQueries {
	public static SchemaValidationInfo Parse(ref DataChunk.Row row) {
		var versionId = row.ReadString();
		var schemaDefinition = row.ReadBlob();
		var dataFormat = (SchemaDataFormat)row.ReadSByte();
		var compatibility = (CompatibilityMode)row.ReadSByte();

		return new() {
			SchemaVersionId = versionId,
			SchemaDefinition = ByteString.CopyFrom(schemaDefinition.Reference.AsMemory().Span),
			DataFormat = dataFormat,
			Compatibility = compatibility,
		};
	}
}
