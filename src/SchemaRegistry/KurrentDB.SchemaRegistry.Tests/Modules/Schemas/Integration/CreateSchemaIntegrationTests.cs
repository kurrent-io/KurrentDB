// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class CreateSchemaIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task registers_initial_version_of_new_schema(CancellationToken cancellationToken) {
		// Arrange
		var expectedEvent = new SchemaCreated {
			SchemaName = NewSchemaName(),
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Description = Faker.Lorem.Text(),
			DataFormat = SchemaFormat.Json,
			Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
			Tags = {
				new Dictionary<string, string> {
					[Faker.Lorem.Word()] = Faker.Lorem.Word(),
					[Faker.Lorem.Word()] = Faker.Lorem.Word(),
					[Faker.Lorem.Word()] = Faker.Lorem.Word()
				}
			},
			SchemaVersionId = Guid.NewGuid().ToString(),
			VersionNumber = 1,
			CreatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		var details = new SchemaDetails {
			Description = expectedEvent.Description,
			DataFormat = expectedEvent.DataFormat,
			Compatibility = expectedEvent.Compatibility,
			Tags = { expectedEvent.Tags }
		};

		// Act
		var createResult = await CreateSchema(
			expectedEvent.SchemaName,
			expectedEvent.SchemaDefinition,
			details,
			cancellationToken
		);

		// Assert
		createResult.Should().NotBeNull();
		createResult.VersionNumber.Should().Be(expectedEvent.VersionNumber);

		var listSchemasResult = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = expectedEvent.SchemaName
			},
			cancellationToken: cancellationToken
		);

		listSchemasResult.Should().NotBeNull();
		listSchemasResult.Schemas.Should().NotBeEmpty();
		listSchemasResult.Schemas.First().LatestSchemaVersion.Should().Be(1);
		listSchemasResult.Schemas.First().SchemaName.Should().Be(expectedEvent.SchemaName);
		listSchemasResult.Schemas.First().Details.Should().BeEquivalentTo(details);
	}
}
