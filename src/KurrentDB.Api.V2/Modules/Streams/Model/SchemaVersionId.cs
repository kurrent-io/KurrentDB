// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api.Streams;

public readonly record struct SchemaVersionId(Guid Value) {
    public static readonly SchemaVersionId None = new(Guid.Empty);

    public Guid Value { get; } = Value;

    public override string ToString() => Value.ToString("D");

    public static SchemaVersionId From(Guid value) {
        return value == Guid.Empty
            ? throw new ArgumentException($"SchemaVersionId '{value}' is not valid", nameof(value))
            : new(value);
    }

    public static SchemaVersionId From(string value) {
        return !Guid.TryParse(value, out var guid)
            ? throw new ArgumentException($"SchemaVersionId '{value}' is not valid", nameof(value))
            : From(guid);
    }

    public static implicit operator SchemaVersionId(Guid _)  => From(_);
    public static implicit operator Guid(SchemaVersionId id) => id.Value;

    public static implicit operator SchemaVersionId(string _)  => From(_);
    public static implicit operator string(SchemaVersionId id) => id.ToString();
}
