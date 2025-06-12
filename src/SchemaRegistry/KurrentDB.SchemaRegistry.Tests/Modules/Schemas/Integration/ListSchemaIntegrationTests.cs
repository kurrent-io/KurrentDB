// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class ListSchemaIntegrationTests : SchemaApplicationTestFixture {
	private const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_schemas_with_prefix(CancellationToken cancellationToken) {
		var prefix = NewPrefix();
		// Arrange
		var schema1 = new SchemaCreated {
			SchemaName = NewSchemaName(prefix),
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Description = Faker.Lorem.Text(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
			Tags = { },
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
			Tags = { },
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

		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schema1.SchemaName,
			SchemaDefinition = schema1.SchemaDefinition,
			Details = details1,
		}, cancellationToken: cancellationToken);

		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schema2.SchemaName,
			SchemaDefinition = schema2.SchemaDefinition,
			Details = details2,
		}, cancellationToken: cancellationToken);

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

	[Test, Timeout(TestTimeoutMs)]
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

		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schema1.SchemaName,
			SchemaDefinition = schema1.SchemaDefinition,
			Details = details1,
		}, cancellationToken: cancellationToken);

		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schema2.SchemaName,
			SchemaDefinition = schema2.SchemaDefinition,
			Details = details2,
		}, cancellationToken: cancellationToken);

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

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schemas_with_prefix(CancellationToken cancellationToken) {
		var prefix = NewPrefix();
		var schemaName1 = NewSchemaName(prefix);
		var schemaName2 = NewSchemaName(prefix);

		// Arrange
		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName1,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
			}
		}, cancellationToken: cancellationToken);


		await Client.RegisterSchemaVersionAsync(new RegisterSchemaVersionRequest {
			SchemaName = schemaName1,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
		}, cancellationToken: cancellationToken);

		var schema1 = await Client.GetSchemaAsync(new GetSchemaRequest {
			SchemaName = schemaName1
		}, cancellationToken: cancellationToken);

		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName2,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
			}
		}, cancellationToken: cancellationToken);

		var schema2 = await Client.GetSchemaAsync(new GetSchemaRequest {
			SchemaName = schemaName2
		}, cancellationToken: cancellationToken);

		var listSchemasResponse = await Client.ListRegisteredSchemasAsync(new ListRegisteredSchemasRequest {
			SchemaNamePrefix = prefix,
		}, cancellationToken: cancellationToken);

		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse.Schemas.Count.Should().Be(2);

		var schemas = listSchemasResponse.Schemas.OrderBy(x => x.RegisteredAt).ToList();

		schemas[0].SchemaName.Should().Be(schemaName1);
		schemas[0].VersionNumber.Should().Be(schema1.Success.Schema.LatestSchemaVersion);

		schemas[1].SchemaName.Should().Be(schema2.Success.Schema.SchemaName);
		schemas[1].VersionNumber.Should().Be(schema2.Success.Schema.LatestSchemaVersion);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schemas_with_tags(CancellationToken cancellationToken) {
		var schemaName1 = NewSchemaName();
		var schemaName2 = NewSchemaName();
		var key = Faker.Lorem.Word();
		var value = Faker.Lorem.Word();

		// Arrange
		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName1,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
				Tags = { new Dictionary<string, string> { [key] = value } }
			},
		}, cancellationToken: cancellationToken);


		await Client.RegisterSchemaVersionAsync(new RegisterSchemaVersionRequest {
			SchemaName = schemaName1,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
		}, cancellationToken: cancellationToken);

		var schema1 = await Client.GetSchemaAsync(new GetSchemaRequest {
			SchemaName = schemaName1
		}, cancellationToken: cancellationToken);

		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName2,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
				Tags = { new Dictionary<string, string> { [key] = value } }
			}
		}, cancellationToken: cancellationToken);

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
		schemas[0].VersionNumber.Should().Be(schema1.Success.Schema.LatestSchemaVersion);

		schemas[1].SchemaName.Should().Be(schema2.Success.Schema.SchemaName);
		schemas[1].VersionNumber.Should().Be(schema2.Success.Schema.LatestSchemaVersion);
	}


	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schemas_with_version_id(CancellationToken cancellationToken) {
		var prefix = NewPrefix();
		var schemaName = NewSchemaName(prefix);

		// Arrange
		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
			}
		}, cancellationToken: cancellationToken);


		var result = await Client.RegisterSchemaVersionAsync(new RegisterSchemaVersionRequest {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
		}, cancellationToken: cancellationToken);

		var schema = await Client.GetSchemaAsync(new GetSchemaRequest {
			SchemaName = schemaName,
		}, cancellationToken: cancellationToken);

		var listSchemasResponse = await Client.ListRegisteredSchemasAsync(new ListRegisteredSchemasRequest {
			SchemaVersionId = result.SchemaVersionId
		}, cancellationToken: cancellationToken);

		listSchemasResponse.Should().NotBeNull();
		listSchemasResponse.Schemas.Count.Should().Be(1);

		listSchemasResponse.Schemas[0].SchemaName.Should().Be(schemaName);
		listSchemasResponse.Schemas[0].VersionNumber.Should().Be(schema.Success.Schema.LatestSchemaVersion);
	}


	[Test, Timeout(TestTimeoutMs)]
	public async Task list_schema_versions(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();

		// Arrange
		var createResult = await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Backward,
				Description = Faker.Lorem.Text(),
			},
		}, cancellationToken: cancellationToken);


		var registerResult = await Client.RegisterSchemaVersionAsync(new RegisterSchemaVersionRequest {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
		}, cancellationToken: cancellationToken);

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

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_schema_versions_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest() {
				SchemaName = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schema_with_version_id_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaVersionId = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schema_with_prefix_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schema_with_tags_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaTags = { new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() } }
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}


	[Test, Timeout(TestTimeoutMs)]
	public async Task list_schemas_with_prefix_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListSchemasAsync(
			new ListSchemasRequest() {
				SchemaNamePrefix = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_schemas_with_tags_not_found(CancellationToken cancellationToken) {
		var response = await Client.ListSchemasAsync(
			new ListSchemasRequest() {
				SchemaTags = { new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() } }
			},
			cancellationToken: cancellationToken);

		response.Schemas.Should().BeEmpty();
	}
}

