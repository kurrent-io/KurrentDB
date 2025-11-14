// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class LookupSchemaNameIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    public async ValueTask lookup_schema_name(CancellationToken cancellationToken) {
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
        var lookupSchemaNameResponse = await Fixture.RegistryClient.LookupSchemaNameAsync(
            new LookupSchemaNameRequest {
                SchemaVersionId = result.SchemaVersionId
            },
            cancellationToken: cancellationToken
        );

        result.ShouldNotBeNull();
        result.SchemaVersionId.ShouldNotBeEmpty();
        lookupSchemaNameResponse.SchemaName.ShouldBe(schemaName);
    }

    [Test]
    public async ValueTask lookup_schema_name_not_found(CancellationToken cancellationToken) {
        var response = async () => await Fixture.RegistryClient.LookupSchemaNameAsync(
            new LookupSchemaNameRequest {
                SchemaVersionId = Guid.NewGuid().ToString()
            },
            cancellationToken: cancellationToken
        );

        var exception = await response.ShouldThrowAsync<RpcException>();
        exception.StatusCode.ShouldBe(StatusCode.NotFound);
    }
}
