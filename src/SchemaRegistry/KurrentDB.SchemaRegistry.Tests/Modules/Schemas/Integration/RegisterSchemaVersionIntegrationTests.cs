// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class RegisterSchemaVersionIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    public async ValueTask registers_new_schema_version_successfully(CancellationToken cancellationToken) {
        // Arrange
        var prefix     = NewSchemaName();
        var schemaName = NewSchemaName(prefix);
        var v1         = NewJsonSchema();
        var v2         = v1.AddOptional("email", JsonObjectType.String);

        await Fixture.CreateSchema(schemaName, v1, cancellationToken);

        // Act
        var registerSchemaVersionResult = await Fixture.RegistryClient.RegisterSchemaVersionAsync(
            new RegisterSchemaVersionRequest {
                SchemaName       = schemaName,
                SchemaDefinition = v2.ToByteString()
            },
            cancellationToken: cancellationToken
        );

        // Assert
        registerSchemaVersionResult.ShouldNotBeNull();
        registerSchemaVersionResult.VersionNumber.ShouldBe(2);
    }

    [Test]
    public async ValueTask throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
        // Arrange
        var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
        var v1                    = NewJsonSchema();

        // Act
        var act = async () => await Fixture.RegistryClient.RegisterSchemaVersionAsync(
            new RegisterSchemaVersionRequest {
                SchemaName       = nonExistentSchemaName,
                SchemaDefinition = v1.ToByteString()
            }, cancellationToken: cancellationToken
        );

        // Assert
        var rex = await act.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async ValueTask throws_exception_when_schema_definition_has_not_changed(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();

        await Fixture.CreateSchema(schemaName, v1, cancellationToken);

        // Act
        var action = async () => await Fixture.RegistryClient.RegisterSchemaVersionAsync(
            new RegisterSchemaVersionRequest {
                SchemaName       = schemaName,
                SchemaDefinition = v1.ToByteString()
            }, cancellationToken: cancellationToken
        );

        // Assert
        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain("Schema definition has not changed");
    }
}
