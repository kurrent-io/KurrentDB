// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using Kurrent.Surge.Schema.Serializers.Json;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Validation;

namespace Kurrent.Surge.Schema.Validation;

[PublicAPI]
public class NJsonSchemaAgent : ISchemaAgent {
    public static readonly ISchemaAgent Instance = new NJsonSchemaAgent();

    public NJsonSchemaAgent(SystemTextJsonSchemaGeneratorSettings? generatorSettings = null, JsonSchemaValidatorSettings? validatorSettings = null) {
        GeneratorSettings = generatorSettings ?? new() {
            SerializerOptions = SystemJsonSchemaSerializerOptions.Default
        };

        ValidatorSettings = validatorSettings ?? new();

        // // this was just using the default serializer options, not the generator settings. could it be a problem?
        // Serializer = new(GeneratorSettings.SerializerOptions);

        Serializer = new();
    }

    SystemTextJsonSchemaGeneratorSettings GeneratorSettings { get; }
    JsonSchemaValidatorSettings           ValidatorSettings { get; }
    SystemJsonSerializer                  Serializer        { get; }

    // Caches for parsed and extracted schemas (not really sure about caching the parsed schemas)
    ConcurrentDictionary<uint, MessageSchema> ParsedSchemaCache    { get; } = new();
    ConcurrentDictionary<Type, MessageSchema> ExtractedSchemaCache { get; } = new();

    public MessageSchema ParseSchema(ReadOnlySpan<char> schemaDefinition) {
        var definition = schemaDefinition.ToString();
        return ParsedSchemaCache.GetOrAdd(
            HashGenerators.FromString.Fnv1a(definition), CreateSchema(),
            (Definition: definition, ValidatorSettings, Serializer)
        );

        static Func<uint, (string Definition, JsonSchemaValidatorSettings ValidatorSettings, SystemJsonSerializer Serializer), MessageSchema> CreateSchema() =>
            static (_, state) => {
                var schema = JsonSchema.FromJsonAsync(state.Definition).GetAwaiter().GetResult();
                return new NJsonMessageSchema(
                    state.Definition, schema,
                    state.ValidatorSettings,
                    state.Serializer
                );
            };
    }

    public MessageSchema ExportSchema(Type type) {
        return ExtractedSchemaCache.GetOrAdd(type, CreateSchema(), (ValidatorSettings, GeneratorSettings, Serializer));

        static Func<Type, (JsonSchemaValidatorSettings ValidatorSettings, SystemTextJsonSchemaGeneratorSettings GeneratorSettings, SystemJsonSerializer Serializer), MessageSchema> CreateSchema() =>
            static (type, state) => {
                var schema = JsonSchema.FromType(type, state.GeneratorSettings);
                return new NJsonMessageSchema(
                    schema.ToJson(), schema,
                    state.ValidatorSettings,
                    state.Serializer
                );
            };
    }
}