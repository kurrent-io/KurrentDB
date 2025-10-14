// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class ListSchemaIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    public async ValueTask list_schemas_with_prefix(CancellationToken cancellationToken) {
        // Arrange
        var prefix = NewPrefix();

        var schema = new SchemaCreated {
            SchemaName       = NewSchemaName(prefix),
            SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
            Description      = Faker.Lorem.Text(),
            DataFormat       = SchemaDataFormat.Json,
            Compatibility    = Faker.Random.Enum(CompatibilityMode.Unspecified),
            SchemaVersionId  = Guid.NewGuid().ToString(),
            VersionNumber    = 1,
            CreatedAt        = Timestamp.FromDateTimeOffset(Fixture.Time.GetUtcNow())
        };

        var details = new SchemaDetails {
            Description   = schema.Description,
            DataFormat    = schema.DataFormat,
            Compatibility = schema.Compatibility,
            Tags          = { schema.Tags }
        };

        await Fixture.CreateSchema(
            schema.SchemaName, schema.SchemaDefinition, details,
            cancellationToken
        );

        var listSchemasResponse = await Fixture.RegistryClient.ListSchemasAsync(
            new ListSchemasRequest {
                SchemaNamePrefix = prefix
            },
            cancellationToken: cancellationToken
        );

        listSchemasResponse.ShouldNotBeNull();
        listSchemasResponse.Schemas
            .ShouldHaveSingleItem()
            .LatestSchemaVersion.ShouldBe(schema.VersionNumber);
    }

    [Test]
    public async ValueTask list_schemas_with_tags(CancellationToken cancellationToken) {
        var key   = Faker.Hacker.Noun();
        var value = Faker.Database.Engine();

        // Arrange
        var schema = new SchemaCreated {
            SchemaName       = NewSchemaName(NewPrefix()),
            SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
            Description      = Faker.Lorem.Text(),
            DataFormat       = SchemaDataFormat.Json,
            Compatibility    = Faker.Random.Enum(CompatibilityMode.Unspecified),
            Tags             = { new Dictionary<string, string> { [key] = value } },
            SchemaVersionId  = Guid.NewGuid().ToString(),
            VersionNumber    = 1,
            CreatedAt        = Timestamp.FromDateTimeOffset(Fixture.Time.GetUtcNow())
        };

        var details = new SchemaDetails {
            Description   = schema.Description,
            DataFormat    = schema.DataFormat,
            Compatibility = schema.Compatibility,
            Tags          = { schema.Tags }
        };

        await Fixture.CreateSchema(
            schema.SchemaName, schema.SchemaDefinition, details,
            cancellationToken
        );

        var listSchemasResponse = await Fixture.RegistryClient.ListSchemasAsync(
            new ListSchemasRequest {
                SchemaTags = { new Dictionary<string, string> { [key] = value } }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        listSchemasResponse
            .Schemas.ShouldHaveSingleItem()
            .SchemaName.ShouldBe(schema.SchemaName);
    }

    [Test]
    public async ValueTask list_registered_schemas_with_tags(CancellationToken cancellationToken) {
        var schemaName = NewSchemaName();
        var key        = Guid.NewGuid().ToString();
        var value      = Guid.NewGuid().ToString();

        var v1 = NewJsonSchema();

        // Arrange
        await Fixture.CreateSchema(
            schemaName, v1,
            new SchemaDetails {
                DataFormat    = SchemaDataFormat.Json,
                Compatibility = CompatibilityMode.Backward,
                Description   = Faker.Lorem.Text(),
                Tags          = { new Dictionary<string, string> { [key] = value } }
            },
            cancellationToken
        );

        var listSchemasResponse = await Fixture.RegistryClient.ListRegisteredSchemasAsync(
            new ListRegisteredSchemasRequest {
                SchemaTags = { new Dictionary<string, string> { [key] = value } }
            },
            cancellationToken: cancellationToken
        );

        // Assert
        listSchemasResponse.ShouldNotBeNull();
        listSchemasResponse
            .Schemas.ShouldHaveSingleItem()
            .SchemaName.ShouldBe(schemaName);
    }

    [Test]
    public async ValueTask list_registered_schemas_with_version_id(CancellationToken cancellationToken) {
        // Arrange
        var prefix     = NewPrefix();
        var schemaName = NewSchemaName(prefix);

        var v1 = NewJsonSchema();

        var createSchemaResult = await Fixture.CreateSchema(schemaName, v1, cancellationToken);

        // Assert
        var listSchemasResponse = await Fixture.RegistryClient.ListRegisteredSchemasAsync(
            new ListRegisteredSchemasRequest {
                SchemaVersionId = createSchemaResult.SchemaVersionId
            },
            cancellationToken: cancellationToken
        );

        listSchemasResponse
            .Schemas.ShouldHaveSingleItem()
            .VersionNumber.ShouldBe(1);
    }

    [Test]
    public async ValueTask list_schema_versions(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1         = NewJsonSchema();

        await Fixture.CreateSchema(schemaName, v1, cancellationToken);

        // Assert
        var listSchemasResponse = await Fixture.RegistryClient.ListSchemaVersionsAsync(
            new ListSchemaVersionsRequest {
                SchemaName = schemaName
            },
            cancellationToken: cancellationToken
        );

        listSchemasResponse
            .Versions.ShouldHaveSingleItem()
            .VersionNumber.ShouldBe(1);
    }

    [Test]
    public async ValueTask list_schema_versions_not_found(CancellationToken cancellationToken) {
        var action = async () => await Fixture.RegistryClient.ListSchemaVersionsAsync(
            new ListSchemaVersionsRequest {
                SchemaName = NewSchemaName()
            },
            cancellationToken: cancellationToken
        );

        var ex = await action.ShouldThrowAsync<RpcException>();
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async ValueTask list_registered_schema_with_version_id_not_found(CancellationToken cancellationToken) {
        var response = await Fixture.RegistryClient.ListRegisteredSchemasAsync(
            new ListRegisteredSchemasRequest {
                SchemaVersionId = Guid.NewGuid().ToString()
            },
            cancellationToken: cancellationToken
        );

        response.Schemas.ShouldBeEmpty();
    }

    [Test]
    public async ValueTask list_registered_schema_with_prefix_not_found(CancellationToken cancellationToken) {
        var response = await Fixture.RegistryClient.ListRegisteredSchemasAsync(
            new ListRegisteredSchemasRequest {
                SchemaNamePrefix = NewPrefix()
            },
            cancellationToken: cancellationToken
        );

        response.Schemas.ShouldBeEmpty();
    }

    [Test]
    public async ValueTask list_schemas_with_prefix_not_found(CancellationToken cancellationToken) {
        var response = await Fixture.RegistryClient.ListSchemasAsync(
            new ListSchemasRequest {
                SchemaNamePrefix = NewPrefix()
            },
            cancellationToken: cancellationToken
        );

        response.Schemas.ShouldBeEmpty();
    }

    [Test]
    public async ValueTask list_schemas_with_tags_not_found(CancellationToken cancellationToken) {
        var response = await Fixture.RegistryClient.ListSchemasAsync(
            new ListSchemasRequest {
                SchemaTags = { new Dictionary<string, string> { [Guid.NewGuid().ToString()] = Guid.NewGuid().ToString() } }
            },
            cancellationToken: cancellationToken
        );

        response.Schemas.ShouldBeEmpty();
    }
}
