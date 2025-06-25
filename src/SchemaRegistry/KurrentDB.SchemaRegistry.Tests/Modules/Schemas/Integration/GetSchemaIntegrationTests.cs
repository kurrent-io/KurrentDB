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

public class GetSchemaIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_newly_created_schema(CancellationToken cancellationToken) {
		// Arrange
		var expected = new SchemaCreated {
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
			Description = expected.Description,
			DataFormat = expected.DataFormat,
			Compatibility = expected.Compatibility,
			Tags = { expected.Tags }
		};

		// Act
		var createResult = await CreateSchema(expected.SchemaName, expected.SchemaDefinition, details, cancellationToken);

		// Assert
		createResult.Should().NotBeNull();
		createResult.VersionNumber.Should().Be(expected.VersionNumber);

		var getSchemaResult = await Client.GetSchemaAsync(
			new GetSchemaRequest {
				SchemaName = expected.SchemaName
			},
			cancellationToken: cancellationToken
		);

		getSchemaResult.Should().NotBeNull();

		getSchemaResult.Schema.LatestSchemaVersion.Should().Be(1);
		getSchemaResult.Schema.SchemaName.Should().Be(expected.SchemaName);
		getSchemaResult.Schema.Details.Should().BeEquivalentTo(details);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_not_found(CancellationToken cancellationToken) {
		var getSchema = async () => await Client.GetSchemaAsync(
			new GetSchemaRequest {
				SchemaName = NewSchemaName()
			},
			cancellationToken: cancellationToken
		);

		var getSchemaException = await getSchema.Should().ThrowAsync<RpcException>();
		getSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
