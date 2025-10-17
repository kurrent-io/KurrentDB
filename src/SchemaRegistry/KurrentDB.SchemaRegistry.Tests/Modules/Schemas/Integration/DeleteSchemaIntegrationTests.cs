// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class DeleteSchemaIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    public async ValueTask deletes_schema_successfully(CancellationToken cancellationToken) {
        // Arrange
        var prefix     = NewPrefix();
        var schemaName = NewSchemaName(prefix);

        await Fixture.CreateSchema(schemaName, cancellationToken);

        // Act
        var deleteSchemaResult = await Fixture.DeleteSchema(schemaName, cancellationToken);

        // Assert
        var listSchemasResult = await Fixture.ListRegisteredSchemas(prefix, cancellationToken);

        deleteSchemaResult.ShouldNotBeNull();
        listSchemasResult.ShouldNotBeNull();
        listSchemasResult.Schemas.ShouldBeEmpty();
    }

    [Test]
    public async ValueTask deletes_schema_with_multiple_versions_successfully(CancellationToken cancellationToken) {
        // Arrange
        var prefix     = NewPrefix();
        var schemaName = NewSchemaName(prefix);

        var v1 = NewJsonSchema();
        var v2 = v1.AddOptional("email", JsonObjectType.String);
        var v3 = v2.AddOptional("age", JsonObjectType.Integer);

        await Fixture.CreateSchema(schemaName, v1, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v3, cancellationToken);

        // Act
        var deleteSchemaResult = await Fixture.DeleteSchema(schemaName, cancellationToken);

        // Assert
        var listSchemasResult = await Fixture.ListRegisteredSchemas(prefix, cancellationToken);

        deleteSchemaResult.ShouldNotBeNull();
        listSchemasResult.ShouldNotBeNull();
        listSchemasResult.Schemas.ShouldBeEmpty();
    }

    [Test]
    public async ValueTask throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
        // Arrange
        var nonExistentSchemaName = NewSchemaName();

        // Act
        var deleteSchema = async () => await Fixture.DeleteSchema(nonExistentSchemaName, cancellationToken);

        // Assert
        var rex = await deleteSchema.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.NotFound);
    }
}
