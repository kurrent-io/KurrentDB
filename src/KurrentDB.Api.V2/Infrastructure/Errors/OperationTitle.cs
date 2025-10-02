// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api.Infrastructure.Errors;

public readonly record struct OperationTitle(string Value) {
    public static readonly OperationTitle None = new("");

    public string Value { get; } = Value;

    public override string ToString() => Value;

    public static OperationTitle From(string value) {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"OperationTitle '{value}' is not valid", nameof(value))
            : new(value);
    }

    public static implicit operator OperationTitle(string _) => From(_);
    public static implicit operator string(OperationTitle _) => _.ToString();
}
