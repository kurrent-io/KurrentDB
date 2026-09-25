// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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

    public override string ToString() => Definition;
}

public static class SchemaAgentExtensions {
    public static MessageSchema ExportSchema<T>(this ISchemaAgent agent) =>
        agent.ExportSchema(typeof(T));
}
