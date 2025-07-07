// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class CreateSchemaIntegrationTests : SchemaApplicationTestFixture {
	[Test]
	public async Task registers_initial_version_of_new_schema(CancellationToken cancellationToken) {
		// Arrange
		string schemaName = NewSchemaName();
		string description = Faker.Lorem.Text();
		var schemaDefinition = NewJsonSchema().ToByteString();
		var compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified);
		var tags = new Dictionary<string, string> {
			[Faker.Lorem.Word()] = Faker.Lorem.Word(),
			[Faker.Lorem.Word()] = Faker.Lorem.Word()
		};

		var details = new SchemaDetails {
			Description = description,
			DataFormat = SchemaFormat.Json,
			Compatibility = compatibility,
			Tags = { tags }
		};

		// Act
		var createResult = await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = schemaDefinition,
			Details = details
		}, cancellationToken: cancellationToken);

		// Assert
		createResult.Should().NotBeNull();
		createResult.VersionNumber.Should().Be(1);
	}
}
