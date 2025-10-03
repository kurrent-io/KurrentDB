// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Text.RegularExpressions;
using EventStore.Plugins.Authorization;

namespace KurrentDB.Api.Infrastructure.Authorization;

[PublicAPI]
public partial record Permission {
    public const string ClaimType = "permission";

    public static readonly Permission None = new Permission {
        Value = null!,
        Claim = null!
    };

    [GeneratedRegex("^[a-zA-Z0-9:_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidationRegex();

    public string    Value     { get; private init; } = null!;
    public Operation Operation { get; private init; }
    public Claim     Claim     { get; private init; } = null!;

    public static implicit operator string(Permission _)    => _.Value;
    public static implicit operator Operation(Permission _) => _.Operation;
    public static implicit operator Claim(Permission _)     => _.Claim;

    public override string ToString() => Value;

    public static T Create<T>(string value, Operation operation) where T : Permission, new() {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Permission value cannot be null or whitespace", nameof(value));

        if (!ValidationRegex().IsMatch(value))
            throw new ArgumentException("Permission contains invalid characters. Only alphanumeric characters and :_.- are allowed.", nameof(value));

        return new T {
            Value     = value,
            Operation = operation,
            Claim     = new Claim(ClaimType, value)
        };
    }
}
