// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using DotNext.Patterns;
using NJsonSchema;
using NJsonSchema.Validation;

namespace Kurrent.Surge.Schema.Validation;

[PublicAPI]
public class NJsonSchemaAgent : ISchemaAgent, ISingleton<NJsonSchemaAgent> {
	public static NJsonSchemaAgent Instance { get; } = new();

    public NJsonSchemaAgent(JsonSchemaValidatorSettings? validatorSettings = null) {
        ValidatorSettings = validatorSettings ?? new();
    }

    public JsonSchemaValidatorSettings           ValidatorSettings { get; }

    // Caches for parsed and extracted schemas (not really sure about caching the parsed schemas)
    ConcurrentDictionary<uint, MessageSchema> ParsedSchemaCache    { get; } = new();

    public MessageSchema ParseSchema(ReadOnlySpan<char> schemaDefinition) {
        var definition = schemaDefinition.ToString();
        return ParsedSchemaCache.GetOrAdd(
            HashGenerators.FromString.Fnv1a(definition), CreateSchema(),
            (Definition: definition, ValidatorSettings)
        );

        static Func<uint, (string Definition, JsonSchemaValidatorSettings ValidatorSettings), MessageSchema> CreateSchema() =>
            static (_, state) => {
                var schema = JsonSchema.FromJsonAsync(state.Definition).GetAwaiter().GetResult();
                return new NJsonMessageSchema(
                    state.Definition, schema,
                    state.ValidatorSettings
                );
            };
    }
}
