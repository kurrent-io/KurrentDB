// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using Shouldly;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class GetSchemaIntegrationTests : SchemaApplicationTestFixture {
	[Test]
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
		await CreateSchema(expected.SchemaName, expected.SchemaDefinition, details, cancellationToken);

		// Assert
		var getSchemaResult = await GetSchema(expected.SchemaName, cancellationToken);
		getSchemaResult.ShouldNotBeNull();
		getSchemaResult.Schema.LatestSchemaVersion.ShouldBe(1);
		getSchemaResult.Schema.SchemaName.ShouldBe(expected.SchemaName);
		getSchemaResult.Schema.Details.ShouldBeEquivalentTo(details);
	}

	[Test]
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
