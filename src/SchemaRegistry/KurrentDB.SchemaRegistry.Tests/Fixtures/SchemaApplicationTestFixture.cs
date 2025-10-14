// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using KurrentDB.Testing.Bogus;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public class SchemaApplicationTestFixture {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerAssembly)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerAssembly)]
    public required ClusterVNodeTestContext Fixture { get; [UsedImplicitly] init; }

    public static string NewPrefix([CallerMemberName] string? name = null) =>
        $"{name.Underscore()}_{GenerateShortId()}".ToLowerInvariant();

    public static JsonSchema NewJsonSchema() {
        return new JsonSchema {
            Type = JsonObjectType.Object,
            Properties = {
                ["id"]   = new JsonSchemaProperty { Type = JsonObjectType.String },
                ["name"] = new JsonSchemaProperty { Type = JsonObjectType.String }
            },
            RequiredProperties = { "id" }
        };
    }

    public static string GenerateShortId() => Identifiers.GenerateShortId();

    public static string NewSchemaName(string? prefix = null, [CallerMemberName] string? name = null) {
        var prefixValue = prefix is null ? string.Empty : $"{prefix}-";
        return $"{prefixValue}{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();
    }
}
