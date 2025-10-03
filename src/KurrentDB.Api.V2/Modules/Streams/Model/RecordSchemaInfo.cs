// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Enum = System.Enum;

namespace KurrentDB.Api.Streams;

/// <summary>
/// Represents information about the schema of a record, including its name, format, and version identifier.
/// </summary>
/// <param name="SchemaName">
/// The name of the schema for the record.
/// </param>
/// <param name="DataFormat">
/// The data format of the schema, such as JSON, Protobuf, Avro, Bytes, or Unspecified.
/// </param>
/// <param name="SchemaVersionId">
/// The unique identifier for the version of the schema.
/// </param>
public record RecordSchemaInfo(SchemaName SchemaName, SchemaFormat DataFormat, SchemaVersionId SchemaVersionId) {
    public static readonly RecordSchemaInfo None = new(SchemaName.None, SchemaFormat.Unspecified, SchemaVersionId.None);

    public bool HasSchemaName      => SchemaName != SchemaName.None;
    public bool HasDataFormat      => DataFormat != SchemaFormat.Unspecified;
    public bool HasSchemaVersionId => SchemaVersionId != SchemaVersionId.None;

    public bool IsValid => HasSchemaName && HasDataFormat;

    public override string ToString() => $"{SchemaName} {DataFormat} {SchemaVersionId}";

    public static RecordSchemaInfo FromProperties(Struct properties) {
        return new RecordSchemaInfo(
            SchemaName:      GetSchemaName(properties),
            DataFormat:      GetSchemaFormat(properties),
            SchemaVersionId: GetSchemaId(properties)
        );

        static SchemaFormat GetSchemaFormat(Struct source) =>
            source.Fields.TryGetValue(Constants.Properties.SchemaFormatKey, out var value)
                ? Enum.Parse<SchemaFormat>(value.StringValue, true)
                : SchemaFormat.Unspecified;

        static SchemaVersionId GetSchemaId(Struct source) =>
            source.Fields.TryGetValue(Constants.Properties.SchemaIdKey, out var value)
                ? SchemaVersionId.From(value.StringValue)
                : SchemaVersionId.None;

        static SchemaName GetSchemaName(Struct source) =>
            source.Fields.TryGetValue(Constants.Properties.SchemaNameKey, out var value)
                ? SchemaName.From(value.StringValue)
                : SchemaName.None;
    }
}
