// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class CheckSchemaCompatibilityIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    public async ValueTask check_schema_compatibility_schema_name_not_found(CancellationToken cancellationToken) {
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();

        var action = async () => await Fixture.CheckSchemaCompatibility(
            schemaName,
            SchemaDataFormat.Json,
            v1,
            cancellationToken
        );

        var ex = await action.ShouldThrowAsync<RpcException>();
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async ValueTask check_schema_compatibility_backward_all_is_compatible(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        var v1 = NewJsonSchema();
        var v2 = v1.Remove("name");
        var v3 = v2.AddOptional("age", JsonObjectType.String);

        await Fixture.CreateSchema(
            schemaName, v1, CompatibilityMode.BackwardAll,
            SchemaDataFormat.Json, cancellationToken
        );

        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);

        // Act
        var response = await Fixture.CheckSchemaCompatibility(
            schemaName, SchemaDataFormat.Json, v3,
            cancellationToken
        );

        // Assert
        response.Success.ShouldNotBeNull();
        response.Success.SchemaVersionId.ShouldNotBeEmpty();
        response.Failure.ShouldBeNull();
    }

    [Test]
    public async ValueTask check_schema_compatibility_backward_all_is_incompatible(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        var v1 = NewJsonSchema()
            .AddOptional("gender", JsonObjectType.String)
            .AddOptional("email", JsonObjectType.String);

        var v2 = v1
            .AddRequired("age", JsonObjectType.Integer)
            .MakeRequired("email")
            .ChangeType("gender", JsonObjectType.Integer);

        await Fixture.CreateSchema(
            schemaName, v1, CompatibilityMode.BackwardAll,
            SchemaDataFormat.Json, cancellationToken
        );

        // Act
        var response = await Fixture.CheckSchemaCompatibility(
            schemaName, SchemaDataFormat.Json, v2,
            cancellationToken
        );

        response.Failure.Errors.ShouldNotBeEmpty();
        response.Failure.Errors.Count.ShouldBe(3);
        response.Failure.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
        response.Failure.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
        response.Failure.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
    }

    [Test]
    public async ValueTask check_schema_compatibility_backward_is_compatible(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        var v1 = NewJsonSchema();
        var v2 = v1.AddOptional("address", JsonObjectType.String);

        await Fixture.CreateSchema(
            schemaName, v1, CompatibilityMode.Backward,
            SchemaDataFormat.Json, cancellationToken
        );

        // Act
        var response = await Fixture.CheckSchemaCompatibility(
            schemaName, SchemaDataFormat.Json, v2,
            cancellationToken
        );

        // Assert
        response.Success.ShouldNotBeNull();
        response.Success.SchemaVersionId.ShouldNotBeEmpty();
        response.Failure.ShouldBeNull();
    }

    [Test]
    public async ValueTask check_schema_compatibility_backward_is_incompatible(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        var v1 = NewJsonSchema();
        var v2 = v1.AddRequired("email", JsonObjectType.String);

        await Fixture.CreateSchema(
            schemaName, v1, CompatibilityMode.Backward,
            SchemaDataFormat.Json, cancellationToken
        );

        // Act
        var response = await Fixture.CheckSchemaCompatibility(
            schemaName, SchemaDataFormat.Json, v2,
            cancellationToken
        );

        // Assert
        response.Failure.Errors.ShouldNotBeEmpty();
        response.Failure.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
    }

    [Test]
    public async ValueTask check_schema_compatibility_forward_is_compatible(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        var v1 = NewJsonSchema()
            .AddOptional("email", JsonObjectType.String)
            .AddOptional("phone", JsonObjectType.String);

        var v2 = v1.Remove("phone");

        await Fixture.CreateSchema(
            schemaName, v1, CompatibilityMode.Forward,
            SchemaDataFormat.Json, cancellationToken
        );

        // Act
        var response = await Fixture.CheckSchemaCompatibility(
            schemaName, SchemaDataFormat.Json, v2,
            cancellationToken
        );

        // Assert
        response.Success.ShouldNotBeNull();
        response.Success.SchemaVersionId.ShouldNotBeEmpty();
        response.Failure.ShouldBeNull();
    }

    [Test]
    public async ValueTask check_schema_compatibility_forward_is_incompatible(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        var v1 = NewJsonSchema();
        var v2 = v1.ChangeType("id", JsonObjectType.Integer);

        await Fixture.CreateSchema(
            schemaName, v1, CompatibilityMode.Forward,
            SchemaDataFormat.Json, cancellationToken
        );

        // Act
        var response = await Fixture.CheckSchemaCompatibility(
            schemaName, SchemaDataFormat.Json, v2,
            cancellationToken
        );

        // Assert
        response.Failure.Errors.ShouldNotBeEmpty();
        response.Failure.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
    }
}
