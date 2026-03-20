// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using NJsonSchema;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility;

static class JsonSchemaCompatibilityFixture {
	static readonly NJsonSchemaCompatibilityManager CompatibilityManager = new();

	public static ValueTask<SchemaCompatibilityResult> CheckCompatibility(
		string uncheckedSchema,
		string referenceSchema,
		SchemaCompatibilityMode mode
	) => CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, mode);

	public static ValueTask<SchemaCompatibilityResult> CheckCompatibility(
		string uncheckedSchema,
		string[] referenceSchemas,
		SchemaCompatibilityMode mode
	) => CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, mode);

	public static JsonSchema NewJsonSchema() =>
		new() {
			Type = JsonObjectType.Object,
			Properties = {
				["id"]   = new JsonSchemaProperty { Type = JsonObjectType.String },
				["name"] = new JsonSchemaProperty { Type = JsonObjectType.String }
			},
			RequiredProperties = { "id" }
		};
}
