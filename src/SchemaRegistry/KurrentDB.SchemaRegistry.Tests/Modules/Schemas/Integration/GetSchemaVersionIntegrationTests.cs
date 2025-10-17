// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class GetSchemaVersionIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    public async ValueTask get_schema_version(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();

        var details = new SchemaDetails {
            Description   = Faker.Lorem.Word(),
            DataFormat    = SchemaDataFormat.Json,
            Compatibility = CompatibilityMode.Backward
        };

        // Act
        var result = await Fixture.CreateSchema(
            schemaName, v1, details,
            cancellationToken
        );

        // Assert
        var response = await Fixture.RegistryClient.GetSchemaVersionAsync(
            new GetSchemaVersionRequest {
                SchemaName    = schemaName,
                VersionNumber = result.VersionNumber
            },
            cancellationToken: cancellationToken
        );

        response.ShouldNotBeNull();
        response.Version.SchemaVersionId.ShouldBe(result.SchemaVersionId);
        response.Version.VersionNumber.ShouldBe(result.VersionNumber);
    }

    [Test]
    public async ValueTask get_schema_version_by_id(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();

        var details = new SchemaDetails {
            Description   = Faker.Lorem.Word(),
            DataFormat    = SchemaDataFormat.Json,
            Compatibility = CompatibilityMode.Backward
        };

        // Act
        var result = await Fixture.CreateSchema(
            schemaName, v1, details,
            cancellationToken
        );

        // Assert
        var response = await Fixture.RegistryClient.GetSchemaVersionByIdAsync(
            new GetSchemaVersionByIdRequest {
                SchemaVersionId = result.SchemaVersionId
            },
            cancellationToken: cancellationToken
        );

        response.ShouldNotBeNull();
        response.Version.SchemaVersionId.ShouldBe(result.SchemaVersionId);
        response.Version.VersionNumber.ShouldBe(result.VersionNumber);
    }

    [Test]
    public async ValueTask get_schema_version_with_stream_name_not_found(CancellationToken cancellationToken) {
        var action = async () => await Fixture.RegistryClient.GetSchemaVersionAsync(
            new GetSchemaVersionRequest {
                SchemaName    = Guid.NewGuid().ToString(),
                VersionNumber = 1
            },
            cancellationToken: cancellationToken
        );

        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async ValueTask get_schema_version_with_version_id_not_found(CancellationToken cancellationToken) {
        var action = async () => await Fixture.RegistryClient.GetSchemaVersionByIdAsync(
            new GetSchemaVersionByIdRequest {
                SchemaVersionId = Guid.NewGuid().ToString()
            },
            cancellationToken: cancellationToken
        );

        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.StatusCode.ShouldBe(StatusCode.NotFound);
    }
}
