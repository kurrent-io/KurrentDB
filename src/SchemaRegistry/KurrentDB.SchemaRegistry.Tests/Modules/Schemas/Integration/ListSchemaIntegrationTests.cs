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

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class ListSchemaIntegrationTests : SchemaApplicationTestFixture {
	[Test]
	public async Task list_schemas_with_prefix(CancellationToken cancellationToken) {
		var prefix = NewPrefix();
		// Arrange
		var schema1 = new SchemaCreated {
			SchemaName = NewSchemaName(prefix),
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Description = Faker.Lorem.Text(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
			SchemaVersionId = Guid.NewGuid().ToString(),
			VersionNumber = 1,
			CreatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		var details1 = new SchemaDetails {
			Description = schema1.Description,
			DataFormat = schema1.DataFormat,
			Compatibility = schema1.Compatibility,
			Tags = { schema1.Tags }
		};

		var schema2 = new SchemaCreated {
			SchemaName = NewSchemaName(prefix),
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Description = Faker.Lorem.Text(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
			SchemaVersionId = Guid.NewGuid().ToString(),
			VersionNumber = 1,
			CreatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		var details2 = new SchemaDetails {
			Description = schema2.Description,
			DataFormat = schema2.DataFormat,
			Compatibility = schema2.Compatibility,
			Tags = { schema2.Tags }
		};

		await CreateSchema(schema1.SchemaName, schema1.SchemaDefinition, details1, cancellationToken);
		await CreateSchema(schema2.SchemaName, schema2.SchemaDefinition, details2, cancellationToken);

		var listSchemasResponse = await Client.ListSchemasAsync(new ListSchemasRequest {
			SchemaNamePrefix = prefix,
		}, cancellationToken: cancellationToken);

		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse.Schemas.Count.Should().Be(2);

		listSchemasResponse.Schemas[0].SchemaName.Should().Be(schema1.SchemaName);
		listSchemasResponse.Schemas[0].LatestSchemaVersion.Should().Be(schema1.VersionNumber);
		listSchemasResponse.Schemas[0].Details.Should().BeEquivalentTo(details1);

		listSchemasResponse.Schemas[1].SchemaName.Should().Be(schema2.SchemaName);
		listSchemasResponse.Schemas[1].LatestSchemaVersion.Should().Be(schema2.VersionNumber);
		listSchemasResponse.Schemas[1].Details.Should().BeEquivalentTo(details2);
	}

	[Test]
	public async Task list_schemas_with_tags(CancellationToken cancellationToken) {
		var key = Faker.Hacker.Noun();
		var value = Faker.Database.Engine();

		// Arrange
		var schema1 = new SchemaCreated {
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

		var details1 = new SchemaDetails {
			Description = schema1.Description,
			DataFormat = schema1.DataFormat,
			Compatibility = schema1.Compatibility,
			Tags = { schema1.Tags }
		};

		var schema2 = new SchemaCreated {
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

		var details2 = new SchemaDetails {
			Description = schema2.Description,
			DataFormat = schema2.DataFormat,
			Compatibility = schema2.Compatibility,
			Tags = { schema2.Tags }
		};

		await CreateSchema(schema1.SchemaName, schema1.SchemaDefinition, details1, cancellationToken);
		await CreateSchema(schema2.SchemaName, schema2.SchemaDefinition, details2, cancellationToken);

		var listSchemasResponse = await Client.ListSchemasAsync(new ListSchemasRequest {
			SchemaTags = { new Dictionary<string, string> { [key] = value } }
		}, cancellationToken: cancellationToken);

		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse.Schemas.Count.Should().Be(2);

		listSchemasResponse.Schemas[0].SchemaName.Should().Be(schema1.SchemaName);
		listSchemasResponse.Schemas[0].LatestSchemaVersion.Should().Be(schema1.VersionNumber);
		listSchemasResponse.Schemas[0].Details.Should().BeEquivalentTo(details1);

		listSchemasResponse.Schemas[1].SchemaName.Should().Be(schema2.SchemaName);
		listSchemasResponse.Schemas[1].LatestSchemaVersion.Should().Be(schema2.VersionNumber);
		listSchemasResponse.Schemas[1].Details.Should().BeEquivalentTo(details2);
	}

	[Test]
	public async Task list_registered_schemas_with_tags(CancellationToken cancellationToken) {
		var schemaName1 = NewSchemaName();
		var schemaName2 = NewSchemaName();
		var key = Faker.Lorem.Word();
		var value = Faker.Lorem.Word();

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		// Arrange
		await CreateSchema(schemaName1, v1,
			new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
				Tags = { new Dictionary<string, string> { [key] = value } }
			},
			cancellationToken
		);

		await RegisterSchemaVersion(schemaName1, v2, cancellationToken);

		var schema1 = await Client.GetSchemaAsync(new GetSchemaRequest {
			SchemaName = schemaName1
		}, cancellationToken: cancellationToken);

		await CreateSchema(schemaName2,
			new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
				Tags = { new Dictionary<string, string> { [key] = value } }
			},
			cancellationToken);

		var schema2 = await Client.GetSchemaAsync(new GetSchemaRequest {
			SchemaName = schemaName2
		}, cancellationToken: cancellationToken);

		var listSchemasResponse = await Client.ListRegisteredSchemasAsync(new ListRegisteredSchemasRequest {
			SchemaTags = { new Dictionary<string, string> { [key] = value } }
		}, cancellationToken: cancellationToken);

		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse.Schemas.Count.Should().Be(2);

		var schemas = listSchemasResponse.Schemas.OrderBy(x => x.RegisteredAt).ToList();

		schemas[0].SchemaName.Should().Be(schemaName1);
		schemas[0].VersionNumber.Should().Be(schema1.Schema.LatestSchemaVersion);

		schemas[1].SchemaName.Should().Be(schema2.Schema.SchemaName);
		schemas[1].VersionNumber.Should().Be(schema2.Schema.LatestSchemaVersion);
	}


	[Test]
	public async Task list_registered_schemas_with_version_id(CancellationToken cancellationToken) {
		var prefix = NewPrefix();
		var schemaName = NewSchemaName(prefix);

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		// Arrange
		await CreateSchema(
			schemaName,
			v1,
			new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
			},
			cancellationToken);

		var result = await RegisterSchemaVersion(schemaName, v2, cancellationToken);

		var schema = await Client.GetSchemaAsync(new GetSchemaRequest {
			SchemaName = schemaName,
		}, cancellationToken: cancellationToken);

		var listSchemasResponse = await Client.ListRegisteredSchemasAsync(new ListRegisteredSchemasRequest {
			SchemaVersionId = result.SchemaVersionId
		}, cancellationToken: cancellationToken);

		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse.Schemas.Count.Should().Be(1);

		listSchemasResponse.Schemas[0].SchemaName.Should().Be(schemaName);
		listSchemasResponse.Schemas[0].VersionNumber.Should().Be(schema.Schema.LatestSchemaVersion);
	}


	[Test]
	public async Task list_schema_versions(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		// Arrange
		var createResult = await CreateSchema(schemaName, v1,
			new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
			},
			cancellationToken);


		var registerResult = await RegisterSchemaVersion(schemaName, v2, cancellationToken);

		var listSchemasResponse = await Client.ListSchemaVersionsAsync(new ListSchemaVersionsRequest {
			SchemaName = schemaName,
		}, cancellationToken: cancellationToken);

		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse.Versions.Count.Should().Be(2);

		var schemas = listSchemasResponse.Versions.OrderBy(x => x.RegisteredAt).ToList();

		schemas[0].VersionNumber.Should().Be(1);
		schemas[0].SchemaVersionId.Should().Be(createResult.SchemaVersionId);

		schemas[1].VersionNumber.Should().Be(2);
		schemas[1].SchemaVersionId.Should().Be(registerResult.SchemaVersionId);
	}

	[Test]
	public async Task list_schema_versions_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);

		if (ex.Which.StatusCode is StatusCode.Internal) {
			Serilog.Log.Logger.Error(ex.Which, "Error");
		}
	}

	[Test]
	public async Task list_registered_schema_with_version_id_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaVersionId = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}

	[Test]
	public async Task list_registered_schema_with_prefix_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}

	[Test]
	public async Task list_registered_schema_with_tags_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaTags = { new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() } }
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}


	[Test]
	public async Task list_schemas_with_prefix_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}

	[Test]
	public async Task list_schemas_with_tags_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaTags = { new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() } }
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}
}
