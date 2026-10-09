// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Nodes;
using NJsonSchema;
using NJsonSchema.Validation;
using SchemaType = NJsonSchema.SchemaType;

namespace Kurrent.Surge.Schema.Validation;

[PublicAPI]
public class NJsonMessageSchema(string schemaDefinition, JsonSchema schema, JsonSchemaValidatorSettings validatorSettings) : MessageSchema(schemaDefinition) {
    internal JsonSchema Schema { get; } = schema;

    JsonSchemaValidatorSettings ValidatorSettings { get; } = validatorSettings;

    public override SchemaValidationResult Validate(string data) {
        try {
            var errors = Schema.Validate(data, SchemaType.JsonSchema, ValidatorSettings);
            return SchemaValidationResult.Failure(errors.Map());
        }
        catch (Exception ex) {
            throw new SchemaValidationException(ex);
        }
    }

    public override SchemaValidationResult Validate(ReadOnlySpan<byte> data) {
        if (data.IsEmpty)
            throw new InvalidOperationException("Data cannot be empty.");

        try {
	        var json = JsonNode.Parse(data)?.ToJsonString() ?? "null";
            var errors = Schema.Validate(json, SchemaType.JsonSchema, ValidatorSettings);
            return SchemaValidationResult.Failure(errors.Map());
        }
        catch (Exception ex) {
            throw new SchemaValidationException(ex);
        }
    }
}
