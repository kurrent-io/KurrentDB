// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Eventuous;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Schema.Serializers;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.Surge.Eventuous;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using ProtocolSchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;

#pragma warning disable CA1822 // Mark members as static

// ReSharper disable InconsistentNaming

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public partial class ClusterVNodeTestContext {
    public async ValueTask<SurgeRecord> CreateRecord<T>(T message, Kurrent.Surge.Schema.SchemaDataFormat dataFormat = Kurrent.Surge.Schema.SchemaDataFormat.Json, string? streamId = null) {
        var schemaName = $"{SchemaRegistryConventions.Streams.RegistryStreamPrefix}-{typeof(T).Name.Kebaberize()}";
        var schemaInfo = new Kurrent.Surge.Schema.SchemaInfo(schemaName, dataFormat);

        var data = await ((ISchemaSerializer)SchemaRegistry).Serialize(message, schemaInfo);

        ulong sequenceId = SequenceIdGenerator.FetchNext();

        var headers = new Headers();

        schemaInfo.InjectIntoHeaders(headers);

        return new SurgeRecord {
            Id = Guid.NewGuid(),
            Position = streamId is null
                ? RecordPosition.ForLog(sequenceId)
                : RecordPosition.ForStream(streamId, StreamRevision.From((long)sequenceId), sequenceId),
            Timestamp  = Time.GetUtcNow().UtcDateTime,
            SchemaInfo = schemaInfo,
            Data       = data,
            Value      = message!,
            ValueType  = typeof(T),
            SequenceId = sequenceId,
            Headers    = headers,
        };
    }

    public async IAsyncEnumerable<SurgeRecord> GenerateRecords<T>(
        int recordCount = 3,
        string? streamId = null,
        Func<int, T, T>? configureMessage = null,
        Func<int, SurgeRecord, SurgeRecord>? configureRecord = null
    ) where T : new() {
        for (var i = 1; i <= recordCount; i++) {
            var message = configureMessage is null ? new T() : configureMessage.Invoke(i, new T());
            var record  = await CreateRecord(message, streamId: streamId);
            yield return configureRecord?.Invoke(i, record) ?? record;
        }
    }

    public async ValueTask<Result<SchemaEntity>.Ok> Apply<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : class {
		var eventStore = Services.GetRequiredService<SystemEventStore>();
		var lookup = Services.GetRequiredService<LookupSchemaNameByVersionId>();

		var application = new SchemaApplication(new NJsonSchemaCompatibilityManager(), lookup, Time.GetUtcNow, eventStore);

		var result = await application.Handle(command, cancellationToken);

		result.ThrowIfError();

		return result.Get()!;
	}

	private static string GenerateShortId() => Identifiers.GenerateShortId();

    public static string NewSchemaName(string? prefix = null, [CallerMemberName] string? name = null) {
		var prefixValue = prefix is null ? string.Empty : $"{prefix}-";
		return $"{prefixValue}{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();
	}

    public static string NewPrefix([CallerMemberName] string? name = null) =>
		$"{name.Underscore()}_{GenerateShortId()}".ToLowerInvariant();

    public static JsonSchema NewJsonSchema() {
		return new JsonSchema {
			Type = JsonObjectType.Object,
			Properties = {
				["id"] = new JsonSchemaProperty { Type = JsonObjectType.String },
				["name"] = new JsonSchemaProperty { Type = JsonObjectType.String }
			},
			RequiredProperties = { "id" }
		};
	}

	// This causes issues with DuckDB. Transaction Rollback
	// async ValueTask WaitUntilCaughtUp(ulong position, CancellationToken ct = default) =>
	// 	await Wait.Until(async () => {
	// 		const string sql =
	// 			"""
	// 			SELECT (SELECT EXISTS (FROM schemas WHERE checkpoint >= $checkpoint))
	// 			    OR (SELECT EXISTS (FROM schema_versions WHERE checkpoint >= $checkpoint))
	// 			""";
	//
	// 		var connection = DuckDbConnectionProvider.GetConnection();
	// 		var parameters = new { checkpoint = position };
	//
	// 		var exists = await Policy<bool>
	// 			.Handle<DuckDBException>()
	// 			.WaitAndRetryAsync(5, _ => TimeSpan.FromMilliseconds(100))
	// 			.ExecuteAsync(async () => await connection.QueryFirstOrDefaultAsync<bool>(sql, parameters));
	//
	// 		return exists;
	// 	}, cancellationToken: ct);

	#region commands

    public async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, CancellationToken cancellationToken = default) =>
		await CreateSchema(schemaName, NewJsonSchema(), CompatibilityMode.None, ProtocolSchemaFormat.Json, cancellationToken);

    public async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, SchemaDetails details, CancellationToken ct = default) =>
		await CreateSchema(schemaName, NewJsonSchema(), details, ct);

    public async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, JsonSchema schemaDefinition, CancellationToken ct = default) =>
		await CreateSchema(schemaName, schemaDefinition, CompatibilityMode.None, ProtocolSchemaFormat.Json, ct);

    public async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, JsonSchema schemaDefinition, SchemaDetails details, CancellationToken ct = default) =>
		await CreateSchema(schemaName, schemaDefinition.ToByteString(), details, ct);

    public async ValueTask<CreateSchemaResponse> CreateSchema(
		string schemaName, JsonSchema schemaDefinition, CompatibilityMode compatibility,  ProtocolSchemaFormat format, CancellationToken ct = default
	) {
		var tags = new Dictionary<string, string> {
			[Faker.Lorem.Word()] = Faker.Lorem.Word(),
			[Faker.Lorem.Word()] = Faker.Lorem.Word()
		};

		var details = new SchemaDetails {
			Compatibility = compatibility,
			DataFormat = format,
			Description = Faker.Lorem.Sentence(),
			Tags = { tags }
		};

		return await CreateSchema(schemaName, schemaDefinition, details, ct);
	}

    public async ValueTask<CreateSchemaResponse> CreateSchema(
		string schemaName, ByteString schemaDefinition, SchemaDetails details, CancellationToken ct = default
	) {
		var command = new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = schemaDefinition,
			Details = details
		};

		var response = await RegistryClient.CreateSchemaAsync(command, cancellationToken: ct);

		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

		return response;
	}

    public async Task<RegisterSchemaVersionResponse> RegisterSchemaVersion(string schemaName, CancellationToken ct = default) =>
		await RegisterSchemaVersion(schemaName, NewJsonSchema(), ct);

    public async Task<RegisterSchemaVersionResponse> RegisterSchemaVersion(string schemaName, JsonSchema schemaDefinition, CancellationToken ct = default) {
		var command = new RegisterSchemaVersionRequest {
			SchemaName = schemaName,
			SchemaDefinition = schemaDefinition.ToByteString()
		};

		var response = await RegistryClient.RegisterSchemaVersionAsync(command, cancellationToken: ct);

		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

		return response;
	}

    public async Task<DeleteSchemaResponse> DeleteSchema(string schemaName, CancellationToken ct = default) {
		var response = await RegistryClient.DeleteSchemaAsync(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken: ct);
		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
		return response;
	}

    public async Task<DeleteSchemaVersionsResponse> DeleteSchemaVersions(string schemaName, IEnumerable<int> versions, CancellationToken ct = default) {
		var response = await RegistryClient.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { versions.ToList() }
			},
			cancellationToken: ct
		);

		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

		return response;
	}

    public async Task<UpdateSchemaResponse> UpdateSchema(string schemaName, SchemaDetails details, FieldMask mask, CancellationToken ct = default) {
		var response = await RegistryClient.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = details,
				UpdateMask = mask
			},
			cancellationToken: ct
		);

		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

		return response;
	}

	#endregion

	#region queries

    public async Task<CheckSchemaCompatibilityResponse> CheckSchemaCompatibility(
		string schemaName, ProtocolSchemaFormat dataFormat, JsonSchema definition, CancellationToken ct = default
	) =>
		await RegistryClient.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = dataFormat,
				Definition = definition.ToByteString()
			},
			cancellationToken: ct
		);

    public async ValueTask<GetSchemaResponse> GetSchema(string schemaName, CancellationToken ct = default) =>
		await RegistryClient.GetSchemaAsync(
			new GetSchemaRequest {
				SchemaName = schemaName
			},
			cancellationToken: ct
		);

    public async Task<ListSchemaVersionsResponse> ListSchemaVersions(string schemaName, CancellationToken ct = default) =>
		await RegistryClient.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = schemaName
			},
			cancellationToken: ct
		);

    public async Task<ListRegisteredSchemasResponse> ListRegisteredSchemas(string prefix, CancellationToken ct = default) =>
		await RegistryClient.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = prefix
			},
			cancellationToken: ct
		);

    public async Task<ListSchemasResponse> ListSchemas(string prefix, CancellationToken ct = default) =>
		await RegistryClient.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = prefix
			},
			cancellationToken: ct
		);

	#endregion
}
