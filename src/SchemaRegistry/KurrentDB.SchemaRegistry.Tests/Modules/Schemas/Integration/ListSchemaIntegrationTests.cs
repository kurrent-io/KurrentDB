// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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
			Tags = {},
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
			Tags = {},
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
}
