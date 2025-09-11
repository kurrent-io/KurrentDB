// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;
using Shouldly;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class ListSchemaIntegrationTests : SchemaApplicationTestFixture {
	[Test]
	public async Task list_schemas_with_prefix(CancellationToken cancellationToken) {
		// Arrange
		var prefix = NewPrefix();

		var schema = new SchemaCreated {
			SchemaName = NewSchemaName(prefix),
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Description = Faker.Lorem.Text(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
			SchemaVersionId = Guid.NewGuid().ToString(),
			VersionNumber = 1,
			CreatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		var details = new SchemaDetails {
			Description = schema.Description,
			DataFormat = schema.DataFormat,
			Compatibility = schema.Compatibility,
			Tags = { schema.Tags }
		};

		await CreateSchema(schema.SchemaName, schema.SchemaDefinition, details, cancellationToken);

		var listSchemasResponse = await Client.ListSchemasAsync(
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
	public async Task list_schemas_with_tags(CancellationToken cancellationToken) {
		var key = Faker.Hacker.Noun();
		var value = Faker.Database.Engine();

		// Arrange
		var schema = new SchemaCreated {
			SchemaName = NewSchemaName(NewPrefix()),
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Description = Faker.Lorem.Text(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
			Tags = { new Dictionary<string, string> { [key] = value } },
			SchemaVersionId = Guid.NewGuid().ToString(),
			VersionNumber = 1,
			CreatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		var details = new SchemaDetails {
			Description = schema.Description,
			DataFormat = schema.DataFormat,
			Compatibility = schema.Compatibility,
			Tags = { schema.Tags }
		};

		await CreateSchema(schema.SchemaName, schema.SchemaDefinition, details, cancellationToken);

		var listSchemasResponse = await Client.ListSchemasAsync(
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
	public async Task list_registered_schemas_with_tags(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();
		var key = Guid.NewGuid().ToString();
		var value = Guid.NewGuid().ToString();

		var v1 = NewJsonSchema();

		// Arrange
		await CreateSchema(schemaName, v1,
			new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
				Tags = { new Dictionary<string, string> { [key] = value } }
			},
			cancellationToken
		);

		var listSchemasResponse = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaTags = { new Dictionary<string, string> { [key] = value } }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse
			.Schemas.ShouldHaveSingleItem()
			.SchemaName.Should().Be(schemaName);
	}

	[Test]
	public async Task list_registered_schemas_with_version_id(CancellationToken cancellationToken) {
		// Arrange
		var prefix = NewPrefix();
		var schemaName = NewSchemaName(prefix);

		var v1 = NewJsonSchema();

		var createSchemaResult = await CreateSchema(schemaName, v1, cancellationToken);

		// Assert
		var listSchemasResponse = await Client.ListRegisteredSchemasAsync(
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
	public async Task list_schema_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchema();

		await CreateSchema(schemaName, v1, cancellationToken);

		// Assert
		var listSchemasResponse = await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = schemaName,
			},
			cancellationToken: cancellationToken
		);

		listSchemasResponse
			.Versions.ShouldHaveSingleItem()
			.VersionNumber.ShouldBe(1);
	}

	[Test]
	public async Task list_schema_versions_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = NewSchemaName()
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.ShouldBe(StatusCode.NotFound);
	}

	[Test]
	public async Task list_registered_schema_with_version_id_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaVersionId = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken);

		response.Schemas.ShouldBeEmpty();
	}

	[Test]
	public async Task list_registered_schema_with_prefix_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = NewPrefix()
			},
			cancellationToken: cancellationToken);

		response.Schemas.ShouldBeEmpty();
	}

	[Test]
	public async Task list_schemas_with_prefix_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = NewPrefix()
			},
			cancellationToken: cancellationToken);

		response.Schemas.ShouldBeEmpty();
	}

	[Test]
	public async Task list_schemas_with_tags_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaTags = { new Dictionary<string, string> { [Guid.NewGuid().ToString()] = Guid.NewGuid().ToString() } }
			},
			cancellationToken: cancellationToken
		);

		response.Schemas.ShouldBeEmpty();
	}
}
