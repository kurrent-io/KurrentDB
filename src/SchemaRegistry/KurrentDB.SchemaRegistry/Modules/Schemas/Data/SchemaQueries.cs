// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Text.Json;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Quack;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure.Grpc;
using KurrentDB.SchemaRegistry.Planes.Storage;
using static KurrentDB.SchemaRegistry.Data.SchemaQueriesMapping;
using static KurrentDB.SchemaRegistry.Data.SchemaSql;
using SchemaCompatibilityError = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityError;
using SchemaCompatibilityErrorKind = KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind;
using SchemaCompatibilityResult = Kurrent.Surge.Schema.Validation.SchemaCompatibilityResult;

namespace KurrentDB.SchemaRegistry.Data;

public class SchemaQueries(IDuckDBConnectionProvider connectionProvider, ISchemaCompatibilityManager compatibilityManager) {
	IDuckDBConnectionProvider ConnectionProvider { get; } = connectionProvider;
	ISchemaCompatibilityManager CompatibilityManager { get; } = compatibilityManager;

	public GetSchemaResponse GetSchema(GetSchemaRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var schema = connection.QueryFirstOrDefault<GetByNameArgs, SchemaRow, GetSchemaByNameQuery>(new(query.SchemaName));
		return
			schema.HasValue
				? new GetSchemaResponse {
					Schema = new() {
						SchemaName = schema.Value.SchemaName,
						LatestSchemaVersion = schema.Value.LatestVersionNumber,
						CreatedAt = Timestamp.FromDateTime(schema.Value.CreatedAt.ToUniversalTime()),
						UpdatedAt = Timestamp.FromDateTime(schema.Value.UpdatedAt.ToUniversalTime()),
						Details = new() {
							Description = schema.Value.Description,
							DataFormat = (SchemaDataFormat)schema.Value.DataFormat,
							Compatibility = (CompatibilityMode)schema.Value.Compatibility,
							Tags = { MapToTags(schema.Value.Tags) }
						}
					}
				}
				: throw RpcExceptions.NotFound("Schema", query.SchemaName);
	}

	public LookupSchemaNameResponse LookupSchemaName(LookupSchemaNameRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var row =
			connection.QueryFirstOrDefault<GetByVersionIdArgs, LookupSchemaNameRow, LookupSchemaNameByVersionIdQuery>(
				new(query.SchemaVersionId));
		return row.HasValue
			? new LookupSchemaNameResponse { SchemaName = row.Value.SchemaName }
			: throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId);
	}

	public GetSchemaVersionResponse GetSchemaVersion(GetSchemaVersionRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		SchemaVersionRow? row = query.HasVersionNumber
			? connection.QueryFirstOrDefault<GetByNameAndNumberArgs, SchemaVersionRow, GetSchemaVersionByNameAndNumberQuery>(
				new(query.SchemaName, query.VersionNumber))
			: connection.QueryFirstOrDefault<GetByNameArgs, SchemaVersionRow, GetLatestSchemaVersionByNameQuery>(new(query.SchemaName));

		if (!row.HasValue)
			return query.HasVersionNumber
				? throw RpcExceptions.NotFound("Schema", query.SchemaName)
				: throw RpcExceptions.NotFound("SchemaVersion", query.VersionNumber.ToString());

		var v = row.Value;
		return new() {
			Version = new() {
				SchemaVersionId = v.VersionId,
				VersionNumber = v.VersionNumber,
				SchemaDefinition = Google.Protobuf.ByteString.CopyFromUtf8(v.SchemaDefinition ?? string.Empty),
				DataFormat = (SchemaDataFormat)v.DataFormat,
				RegisteredAt = Timestamp.FromDateTime(v.RegisteredAt.ToUniversalTime())
			}
		};
	}

	public GetSchemaVersionByIdResponse GetSchemaVersionById(GetSchemaVersionByIdRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var row =
			connection.QueryFirstOrDefault<GetByVersionIdArgs, SchemaVersionRow, GetSchemaVersionByIdQuery>(new(query.SchemaVersionId));
		if (!row.HasValue)
			throw RpcExceptions.NotFound("SchemaVersion", query.SchemaVersionId);

		var v = row.Value;
		return new() {
			Version = new() {
				SchemaVersionId = v.VersionId,
				VersionNumber = v.VersionNumber,
				SchemaDefinition = Google.Protobuf.ByteString.CopyFromUtf8(v.SchemaDefinition ?? string.Empty),
				DataFormat = (SchemaDataFormat)v.DataFormat,
				RegisteredAt = Timestamp.FromDateTime(v.RegisteredAt.ToUniversalTime())
			}
		};
	}

	public ListSchemasResponse ListSchemas(ListSchemasRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new ListSchemasArgs(
			query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : string.Empty,
			query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags) : string.Empty
		);
		var result = connection.QueryToList<ListSchemasArgs, SchemaRow, ListSchemasQuery>(args);

		var list = result.Select(row => new Schema {
			SchemaName = row.SchemaName,
			LatestSchemaVersion = row.LatestVersionNumber,
			CreatedAt = Timestamp.FromDateTime(row.CreatedAt.ToUniversalTime()),
			UpdatedAt = Timestamp.FromDateTime(row.UpdatedAt.ToUniversalTime()),
			Details = new() {
				Description = row.Description, DataFormat = (SchemaDataFormat)row.DataFormat,
				Compatibility = (CompatibilityMode)row.Compatibility, Tags = { MapToTags(row.Tags) }
			}
		});

		return new() { Schemas = { list } };
	}

	public ListSchemaVersionsResponse ListSchemaVersions(ListSchemaVersionsRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var versions = new List<SchemaVersion>();
		if (query.IncludeDefinition) {
			versions.AddRange(connection
				.QueryToList<GetByNameArgs, SchemaVersionRow, ListSchemaVersionsIncludeDefinitionQuery>(new(query.SchemaName))
				.Select(row => new SchemaVersion {
					SchemaVersionId = row.VersionId,
					VersionNumber = row.VersionNumber,
					SchemaDefinition = Google.Protobuf.ByteString.CopyFromUtf8(row.SchemaDefinition),
					DataFormat = (SchemaDataFormat)row.DataFormat,
					RegisteredAt = Timestamp.FromDateTime(row.RegisteredAt.ToUniversalTime())
				}));
		} else {
			versions.AddRange(connection
				.QueryToList<GetByNameArgs, SchemaVersionHeaderRow, ListSchemaVersionsExcludeDefinitionQuery>(new(query.SchemaName))
				.Select(row => new SchemaVersion {
					SchemaVersionId = row.VersionId,
					VersionNumber = row.VersionNumber,
					SchemaDefinition = Google.Protobuf.ByteString.Empty,
					DataFormat = (SchemaDataFormat)row.DataFormat,
					RegisteredAt = Timestamp.FromDateTime(row.RegisteredAt.ToUniversalTime())
				}));
		}

		return versions.Count == 0
			? throw RpcExceptions.NotFound("Schema", query.SchemaName)
			: new() { Versions = { versions } };
	}

	public ListRegisteredSchemasResponse ListRegisteredSchemas(ListRegisteredSchemasRequest query) {
		using var scope = ConnectionProvider.GetScopedConnection(out var connection);

		var args = new ListRegisteredSchemasArgs(
			query.SchemaVersionId,
			query.HasSchemaNamePrefix ? $"{query.SchemaNamePrefix}%" : string.Empty,
			query.SchemaTags.Count > 0 ? JsonSerializer.Serialize(query.SchemaTags) : string.Empty
		);

		var result = connection
			.QueryToList<ListRegisteredSchemasArgs, RegisteredSchemaRow, ListRegisteredSchemasByArgsQuery>(args);
		var list = result.Select(row => new RegisteredSchema {
			SchemaName = row.SchemaName,
			DataFormat = (SchemaDataFormat)row.DataFormat,
			Compatibility = (CompatibilityMode)row.Compatibility,
			Tags = { MapToTags(row.Tags) },
			SchemaVersionId = row.VersionId,
			SchemaDefinition = Google.Protobuf.ByteString.CopyFromUtf8(row.SchemaDefinition ?? string.Empty),
			VersionNumber = row.VersionNumber,
			RegisteredAt = Timestamp.FromDateTime(row.RegisteredAt.ToUniversalTime()),
		});

		return new() { Schemas = { list } };
	}

	public async Task<CheckSchemaCompatibilityResponse> CheckSchemaCompatibility(CheckSchemaCompatibilityRequest query,
		CancellationToken cancellationToken) {
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
			result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, info.SchemaDefinition.ToStringUtf8(), compatibility,
				cancellationToken);
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
		var row =
			connection.QueryFirstOrDefault<GetByNameArgs, SchemaValidationInfoRow, GetLatestSchemaValidationInfoByNameQuery>(
				new(schemaName));
		return !row.HasValue ? throw RpcExceptions.NotFound("Schema", schemaName) : row.Value.Map();
	}

	static SchemaValidationInfo GetLatestSchemaValidationInfo(DuckDBAdvancedConnection connection, Guid schemaVersionId) {
		var row =
			connection.QueryFirstOrDefault<GetByVersionIdArgs, SchemaValidationInfoRow, GetLatestSchemaValidationInfoByVersionIdQuery>(
				new(schemaVersionId.ToString()));
		return !row.HasValue ? throw RpcExceptions.NotFound("SchemaVersion", schemaVersionId.ToString()) : row.Value.Map();
	}

	static IEnumerable<SchemaValidationInfo> GetAllSchemaValidationInfos(DuckDBAdvancedConnection connection, string schemaName) {
		var result = connection
			.QueryToList<GetByNameArgs, SchemaValidationInfoRow, GetAllSchemaValidationInfosByNameQuery>(new(schemaName));
		return result.Select(row => row.Map());
	}

	static IEnumerable<SchemaValidationInfo> GetAllSchemaValidationInfos(DuckDBAdvancedConnection connection, Guid schemaVersionId) {
		var result =
			connection.QueryToList<GetByVersionIdArgs, SchemaValidationInfoRow, GetAllSchemaValidationInfosByVersionIdQuery>(
				new(schemaVersionId.ToString()));

		return result.Select(row => row.Map());
	}
}

file static class Mappings {
	public static SchemaValidationInfo Map(this SchemaValidationInfoRow row)
		=> new() {
			SchemaVersionId = row.VersionId,
			SchemaDefinition = Google.Protobuf.ByteString.CopyFromUtf8(row.SchemaDefinition),
			DataFormat = (SchemaDataFormat)row.DataFormat,
			Compatibility = (CompatibilityMode)row.Compatibility
		};
}
