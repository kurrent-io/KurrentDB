// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using Shouldly;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class GetSchemaIntegrationTests : SchemaApplicationTestFixture {
	private const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_newly_created_schema(CancellationToken cancellationToken) {
		// Arrange
		var expectedEvent = new SchemaCreated {
			SchemaName = NewSchemaName(),
			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
			Description = Faker.Lorem.Text(),
			DataFormat = SchemaDataFormat.Json,
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
		var createResult = await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = expectedEvent.SchemaName,
				SchemaDefinition = expectedEvent.SchemaDefinition,
				Details = details
			},
			cancellationToken: cancellationToken
		);

		// Assert
		createResult.Should().NotBeNull();
		createResult.VersionNumber.Should().Be(expectedEvent.VersionNumber);

		var getSchemaResult = await Client.GetSchemaAsync(
			new GetSchemaRequest {
				SchemaName = expectedEvent.SchemaName
			},
			cancellationToken: cancellationToken
		);

		getSchemaResult.Should().NotBeNull();

		getSchemaResult.Schema.LatestSchemaVersion.Should().Be(1);
		getSchemaResult.Schema.SchemaName.Should().Be(expectedEvent.SchemaName);
		getSchemaResult.Schema.Details.Should().BeEquivalentTo(details);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_not_found(CancellationToken cancellationToken) {
		var result = await Client.GetSchemaAsync(
			new GetSchemaRequest {
				SchemaName = Guid.NewGuid().ToString(),
			},
			cancellationToken: cancellationToken
		);

		result.Schema.ShouldBeNull();
	}
}
