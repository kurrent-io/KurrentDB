// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kurrent.Surge.Schema.Validation;


/// <summary>
/// Defines an agent capable of parsing and exporting message schemas.
/// </summary>
public interface ISchemaAgent
{
    /// <summary>
    /// Parses a schema definition from a ReadOnlySpan of characters.
    /// </summary>
    /// <param name="schemaDefinition">The schema definition as a ReadOnlySpan of characters.</param>
    /// <returns>A MessageSchema representing the parsed schema.</returns>
    MessageSchema ParseSchema(ReadOnlySpan<char> schemaDefinition);

    /// <summary>
    /// Exports a message schema based on the provided Type.
    /// </summary>
    /// <param name="type">The Type for which to export the schema.</param>
    /// <returns>A MessageSchema representing the exported schema.</returns>
    MessageSchema ExportSchema(Type type);
}

public abstract class MessageSchema(string schemaDefinition) {
    public string Definition { get; } = Ensure.NotNullOrEmpty(schemaDefinition);

    public abstract SchemaValidationResult Validate(string data);

    public abstract SchemaValidationResult Validate(ReadOnlySpan<byte> data);

    public abstract SchemaValidationResult Validate<T>(T data) where T : class;

    public abstract SchemaCompatibilityResult IsCompatible(MessageSchema schema, SchemaCompatibilityMode mode);

    public override string ToString() => Definition;
}

public static class SchemaAgentExtensions {
    public static MessageSchema ExportSchema<T>(this ISchemaAgent agent) =>
        agent.ExportSchema(typeof(T));

    public static SchemaValidationResult Validate(this ISchemaAgent agent, ReadOnlySpan<byte> data, string schemaDefinition) =>
        agent.ParseSchema(schemaDefinition).Validate(data);
}

public static class MessageSchemaExtensions {
    public static SchemaValidationResult Validate(this MessageSchema schema, ReadOnlySpan<char> data) =>
        schema.Validate(data.ToString());

    public static SchemaValidationResult Validate(this MessageSchema schema, JsonDocument data) =>
        schema.Validate(data.ToString() ?? string.Empty);

    public static SchemaValidationResult Validate(this MessageSchema schema, JsonNode data) =>
        schema.Validate(data.ToJsonString());
}