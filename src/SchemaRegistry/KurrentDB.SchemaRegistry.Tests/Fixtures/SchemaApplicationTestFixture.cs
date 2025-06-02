// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Eventuous;
using Google.Protobuf;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.Surge.Eventuous;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;

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
}

public static class JsonSchemaExtensions {
	public static ByteString ToByteString(this JsonSchema schema)
		=> ByteString.CopyFromUtf8(schema.ToJson());

	public static JsonSchema AddOptional(this JsonSchema schema, string name, JsonObjectType type) {
		var clone = Clone(schema);
		clone.Properties[name] = new JsonSchemaProperty {
			Type = type
		};
		clone.RequiredProperties.Remove(name);
		return clone;
	}

	public static JsonSchema AddRequired(this JsonSchema schema, string name, JsonObjectType type) {
		var clone = Clone(schema);
		clone.Properties[name] = new JsonSchemaProperty {
			Type = type
		};
		if (!clone.RequiredProperties.Contains(name))
			clone.RequiredProperties.Add(name);
		return clone;
	}

	public static JsonSchema SetRequired(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.RequiredProperties.Add(name);
		return clone;
	}

	public static JsonSchema RemoveOptional(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.Properties.Remove(name);
		clone.RequiredProperties.Remove(name);
		return clone;
	}

	public static JsonSchema RemoveRequired(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.RequiredProperties.Remove(name);
		return clone;
	}

	public static JsonSchema SetType(this JsonSchema schema, string name, JsonObjectType newType) {
		var clone = Clone(schema);

		if (!clone.Properties.TryGetValue(name, out var property))
			throw new ArgumentException($"Property '{name}' does not exist in the schema");

		property.Type = newType;
		return clone;
	}

	static JsonSchema Clone(JsonSchema original) => JsonSchema.FromJsonAsync(original.ToJson()).GetAwaiter().GetResult();
}
