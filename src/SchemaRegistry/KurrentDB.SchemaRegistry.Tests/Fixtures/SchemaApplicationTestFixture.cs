// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Runtime.CompilerServices;
using Dapper;
using DuckDB.NET.Data;
using Eventuous;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.Surge.Eventuous;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using Polly;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public abstract class SchemaApplicationTestFixture : SchemaRegistryServerTestFixture {
	protected async ValueTask<Result<SchemaEntity>.Ok> Apply<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : class {
		var eventStore = NodeServices.GetRequiredService<SystemEventStore>();
		var lookup = NodeServices.GetRequiredService<LookupSchemaNameByVersionId>();

		var application = new SchemaApplication(new NJsonSchemaCompatibilityManager(), lookup, TimeProvider.GetUtcNow, eventStore);

		var result = await application.Handle(command, cancellationToken);

		result.ThrowIfError();

		return result.Get()!;
	}

	private static string GenerateShortId() => Identifiers.GenerateShortId();

	protected static string NewSchemaName(string? prefix = null, [CallerMemberName] string? name = null) {
		var prefixValue = prefix is null ? string.Empty : $"{prefix}-";
		return $"{prefixValue}{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();
	}

	protected static string NewPrefix([CallerMemberName] string? name = null) =>
		$"{name.Underscore()}_{GenerateShortId()}".ToLowerInvariant();

	protected static JsonSchema NewJsonSchema() {
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

	protected async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, CancellationToken cancellationToken = default) =>
		await CreateSchema(schemaName, NewJsonSchema(), CompatibilityMode.None, SchemaFormat.Json, cancellationToken);

	protected async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, SchemaDetails details, CancellationToken ct = default) =>
		await CreateSchema(schemaName, NewJsonSchema(), details, ct);

	protected async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, JsonSchema schemaDefinition, CancellationToken ct = default) =>
		await CreateSchema(schemaName, schemaDefinition, CompatibilityMode.None, SchemaFormat.Json, ct);

	protected async ValueTask<CreateSchemaResponse> CreateSchema(string schemaName, JsonSchema schemaDefinition, SchemaDetails details,
		CancellationToken ct = default) =>
		await CreateSchema(schemaName, schemaDefinition.ToByteString(), details, ct);

	protected async ValueTask<CreateSchemaResponse> CreateSchema(
		string schemaName, JsonSchema schemaDefinition, CompatibilityMode compatibility, SchemaFormat format, CancellationToken ct = default
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

		var response = await CreateSchema(schemaName, schemaDefinition, details, ct);
		// TODO: Remove this delay after figuring out how to track the projection progress
		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
		return response;
	}

	protected async ValueTask<CreateSchemaResponse> CreateSchema(
		string schemaName, ByteString schemaDefinition, SchemaDetails details, CancellationToken ct = default
	) {
		var command = new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = schemaDefinition,
			Details = details
		};

		var response = await Client.CreateSchemaAsync(command, cancellationToken: ct);

		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

		return response;
	}

	protected async Task<RegisterSchemaVersionResponse> RegisterSchemaVersion(string schemaName, CancellationToken ct = default) =>
		await RegisterSchemaVersion(schemaName, NewJsonSchema(), ct);

	protected async Task<RegisterSchemaVersionResponse> RegisterSchemaVersion(string schemaName, JsonSchema schemaDefinition, CancellationToken ct = default) {
		var command = new RegisterSchemaVersionRequest {
			SchemaName = schemaName,
			SchemaDefinition = schemaDefinition.ToByteString()
		};

		var response = await Client.RegisterSchemaVersionAsync(command, cancellationToken: ct);

		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

		return response;
	}

	protected async Task<DeleteSchemaResponse> DeleteSchema(string schemaName, CancellationToken ct = default) {
		var response = await Client.DeleteSchemaAsync(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken: ct);
		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
		return response;
	}

	protected async Task<DeleteSchemaVersionsResponse> DeleteSchemaVersions(string schemaName, IEnumerable<int> versions, CancellationToken ct = default) {
		var response = await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { versions.ToList() }
			},
			cancellationToken: ct
		);

		await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

		return response;
	}

	protected async Task<UpdateSchemaResponse> UpdateSchema(string schemaName, SchemaDetails details, FieldMask mask, CancellationToken ct = default) {
		var response = await Client.UpdateSchemaAsync(
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

	protected async Task<CheckSchemaCompatibilityResponse> CheckSchemaCompatibility(
		string schemaName, SchemaFormat dataFormat, JsonSchema definition, CancellationToken ct = default
	) =>
		await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = dataFormat,
				Definition = definition.ToByteString()
			},
			cancellationToken: ct
		);

	protected async ValueTask<GetSchemaResponse> GetSchema(string schemaName, CancellationToken ct = default) =>
		await Client.GetSchemaAsync(
			new GetSchemaRequest {
				SchemaName = schemaName
			},
			cancellationToken: ct
		);

	protected async Task<ListSchemaVersionsResponse> ListSchemaVersions(string schemaName, CancellationToken ct = default) =>
		await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = schemaName
			},
			cancellationToken: ct
		);

	protected async Task<ListRegisteredSchemasResponse> ListRegisteredSchemas(string prefix, CancellationToken ct = default) =>
		await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = prefix
			},
			cancellationToken: ct
		);

	protected async Task<ListSchemasResponse> ListSchemas(string prefix, CancellationToken ct = default) =>
		await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = prefix
			},
			cancellationToken: ct
		);

	#endregion
}

public static class JsonSchemaExtensions {
	public static ByteString ToByteString(this JsonSchema schema) => ByteString.CopyFromUtf8(schema.ToJson());

	static JsonSchema AddField(this JsonSchema schema, string name, JsonObjectType type, object? defaultValue = null, bool? required = false) {
		var clone = Clone(schema);
		clone.Properties[name] = new JsonSchemaProperty {
			Type = type,
			Default = defaultValue
		};
		switch (required) {
			case true when !clone.RequiredProperties.Contains(name):
				clone.RequiredProperties.Add(name);
				break;
			case false:
				clone.RequiredProperties.Remove(name);
				break;
		}

		return clone;
	}

	public static JsonSchema AddOptional(this JsonSchema schema, string name, JsonObjectType type, object? defaultValue = null) =>
		AddField(schema, name, type, defaultValue, required: false);

	public static JsonSchema AddRequired(this JsonSchema schema, string name, JsonObjectType type, object? defaultValue = null) =>
		AddField(schema, name, type, defaultValue, required: true);

	public static JsonSchema MakeRequired(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.RequiredProperties.Add(name);
		return clone;
	}

	public static JsonSchema Remove(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.Properties.Remove(name);
		clone.RequiredProperties.Remove(name);
		return clone;
	}

	public static JsonSchema MakeOptional(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.RequiredProperties.Remove(name);
		return clone;
	}

	public static JsonSchema ChangeType(this JsonSchema schema, string name, JsonObjectType newType) {
		var clone = Clone(schema);

		if (!clone.Properties.TryGetValue(name, out var property))
			throw new ArgumentException($"Property '{name}' does not exist in the schema");

		property.Type = newType;
		return clone;
	}

	public static JsonSchema WidenType(this JsonSchema schema, string name, JsonObjectType additionalType) {
		var clone = Clone(schema);

		if (!clone.Properties.TryGetValue(name, out var property))
			throw new ArgumentException($"Property '{name}' does not exist in the schema");

		property.Type |= additionalType;
		return clone;
	}

	static JsonSchema Clone(JsonSchema original) => JsonSchema.FromJsonAsync(original.ToJson()).GetAwaiter().GetResult();
}
