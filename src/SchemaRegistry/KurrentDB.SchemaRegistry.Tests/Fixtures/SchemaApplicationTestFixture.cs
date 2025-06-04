// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Eventuous;
using Google.Protobuf;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.Surge.Eventuous;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
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

	protected static string NewSchemaName() => $"test-schema-{Guid.NewGuid():N}";

	protected static JsonSchema NewJsonSchemaDefinition() {
		return JsonSchema.FromJsonAsync(
			"""
			{
			    "type": "object",
			    "properties": {
			        "id": { "type": "string" },
			        "name": { "type": "string" }
			    },
			    "required": ["id"]
			}
			"""
		).GetAwaiter().GetResult();
	}

	protected CreateSchemaRequest CreateSchemaRequest(
		string? schemaName = null,
		SchemaFormat dataFormat = SchemaFormat.Json,
		CompatibilityMode compatibility = CompatibilityMode.Backward,
		string? description = null,
		Dictionary<string, string>? tags = null,
		ByteString? schemaDefinition = null) {
		schemaName ??= NewSchemaName();
		description ??= Faker.Lorem.Sentence();
		tags ??= new Dictionary<string, string> { ["env"] = "test" };
		schemaDefinition ??= NewJsonSchemaDefinition().ToByteString();

		return new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = schemaDefinition,
			Details = new SchemaDetails {
				Description = description,
				DataFormat = dataFormat,
				Compatibility = compatibility,
				Tags = { tags }
			}
		};
	}

	protected async Task<CreateSchemaResponse> CreateSchemaAsync(
		string? schemaName = null,
		SchemaFormat dataFormat = SchemaFormat.Json,
		CompatibilityMode compatibility = CompatibilityMode.None,
		string? description = null,
		Dictionary<string, string>? tags = null,
		ByteString? schemaDefinition = null,
		CancellationToken cancellationToken = default) {
		schemaName ??= NewSchemaName();
		description ??= Faker.Lorem.Sentence();
		tags ??= new Dictionary<string, string> { ["env"] = "test" };
		schemaDefinition ??= NewJsonSchemaDefinition().ToByteString();

		var result = await Client.CreateSchemaAsync(
			request: CreateSchemaRequest(schemaName, dataFormat, compatibility, description, tags, schemaDefinition),
			cancellationToken: cancellationToken
		);

		return result;
	}
}

public static class JsonSchemaExtensions {
	public static ByteString ToByteString(this JsonSchema schema)
		=> ByteString.CopyFromUtf8(schema.ToJson());

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
