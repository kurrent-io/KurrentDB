// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class DeleteSchemaVersionsIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    [Arguments(CompatibilityMode.None)]
    [Arguments(CompatibilityMode.Backward)]
    public async ValueTask delete_versions_successfully(CompatibilityMode compatibility, CancellationToken cancellationToken) {
        // Arrange
        var prefix     = NewPrefix();
        var schemaName = NewSchemaName(prefix);
        var v1         = NewJsonSchema();
        var v2         = v1.AddOptional("email", JsonObjectType.String);
        var v3         = v2.AddOptional("age", JsonObjectType.Integer);

        await Fixture.CreateSchema(
            schemaName, v1, compatibility,
            SchemaDataFormat.Json, cancellationToken
        );

        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v3, cancellationToken);

        // Act
        var schemaVersionsResult = await Fixture.RegistryClient.DeleteSchemaVersionsAsync(
            new DeleteSchemaVersionsRequest {
                SchemaName = schemaName,
                Versions   = { new List<int> { 1, 2 } }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        schemaVersionsResult.ShouldNotBeNull();
        schemaVersionsResult.Errors.ShouldBeEmpty();

        // var listRegisteredSchemasResult = await ListRegisteredSchemas(prefix, cancellationToken);
        //
        // listRegisteredSchemasResult.Schemas
        // 	.ShouldHaveSingleItem()
        // 	.VersionNumber.ShouldBe(3);
    }

    [Test]
    public async ValueTask throws_not_found_for_non_existing_schema(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();
        var v2         = v1.AddOptional("email", JsonObjectType.String);

        await Fixture.CreateSchema(schemaName, v1, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);
        await Fixture.DeleteSchema(schemaName, cancellationToken);

        // Act
        var deleteVersions = async () => await Fixture.RegistryClient.DeleteSchemaVersionsAsync(
            new DeleteSchemaVersionsRequest {
                SchemaName = schemaName,
                Versions   = { 1 }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        var rex = await deleteVersions.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async ValueTask throws_precondition_when_deleting_all_versions(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        await Fixture.CreateSchema(schemaName, cancellationToken);

        // Act
        var deleteVersions = async () => await Fixture.RegistryClient.DeleteSchemaVersionsAsync(
            new DeleteSchemaVersionsRequest {
                SchemaName = schemaName,
                Versions   = { 1 }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        var rex = await deleteVersions.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain($"Cannot delete all versions of schema {schemaName}");
    }

    [Test]
    [Arguments(CompatibilityMode.Forward)]
    [Arguments(CompatibilityMode.Full)]
    public async ValueTask throws_precondition_for_restricted_compatibility_modes(
        CompatibilityMode compatibilityMode, CancellationToken cancellationToken
    ) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();
        var v2         = v1.AddOptional("email", JsonObjectType.String);

        await Fixture.CreateSchema(
            schemaName, v1, compatibilityMode,
            SchemaDataFormat.Json, cancellationToken
        );

        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);

        // Act
        var deleteVersion = async () => await Fixture.RegistryClient.DeleteSchemaVersionsAsync(
            new DeleteSchemaVersionsRequest {
                SchemaName = schemaName,
                Versions   = { 1 }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        var rex = await deleteVersion.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain($"Cannot delete versions of schema {schemaName} in {compatibilityMode} compatibility mode");
    }

    [Test]
    public async ValueTask throws_precondition_when_deleting_latest_in_backward_mode(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();
        var v2         = v1.AddOptional("email", JsonObjectType.String);

        await Fixture.CreateSchema(
            schemaName, v1, CompatibilityMode.Backward,
            SchemaDataFormat.Json, cancellationToken
        );

        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);

        // Act
        var deleteLatestVersion = async () => await Fixture.RegistryClient.DeleteSchemaVersionsAsync(
            new DeleteSchemaVersionsRequest {
                SchemaName = schemaName,
                Versions   = { 2 }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        var rex = await deleteLatestVersion.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain($"Cannot delete the latest version of schema {schemaName} in Backward compatibility mode");
    }

    [Test]
    public async ValueTask throws_precondition_for_non_existing_versions(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();
        var v2         = v1.AddOptional("email", JsonObjectType.String);

        await Fixture.CreateSchema(schemaName, v1, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);

        var nonExistentVersions = new List<int> { 3, 4 };

        // Act
        var deleteVersions = async () => await Fixture.RegistryClient.DeleteSchemaVersionsAsync(
            new DeleteSchemaVersionsRequest {
                SchemaName = schemaName,
                Versions   = { nonExistentVersions }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        var rex = await deleteVersions.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain($"Schema {schemaName} does not have versions: 3, 4");
    }
}
