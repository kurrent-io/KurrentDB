// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Kurrent.Surge.Core.Tests.Schema.Validation;

internal static class SchemaAgentExtensions {
	public static MessageSchema ExportSchema<T>(this NJsonSchemaAgent agent, SystemTextJsonSchemaGeneratorSettings settings) {
		var schema = JsonSchema.FromType(typeof(T), settings);
		return new NJsonMessageSchema(
			schema.ToJson(), schema,
			agent.ValidatorSettings);
	}
}
