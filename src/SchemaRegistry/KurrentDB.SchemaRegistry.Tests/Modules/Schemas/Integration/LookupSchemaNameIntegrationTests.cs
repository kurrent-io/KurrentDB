// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using Shouldly;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class LookupSchemaNameIntegrationTests : SchemaApplicationTestFixture {
	[Test]
	public async Task lookup_schema_name(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();

		var details = new SchemaDetails {
			Description = Faker.Lorem.Word(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = CompatibilityMode.Backward
		};

		// Act
		var result = await CreateSchema(schemaName, schemaDefinition: v1, details, cancellationToken);

		result.Should().NotBeNull();
		result.SchemaVersionId.Should().NotBeEmpty();

		// Assert
		var lookupSchemaNameResponse = await Client.LookupSchemaNameAsync(
			new LookupSchemaNameRequest {
				SchemaVersionId = result.SchemaVersionId,
			},
			cancellationToken: cancellationToken
		);

		lookupSchemaNameResponse.Should().NotBeNull();
		lookupSchemaNameResponse.SchemaName.Should().Be(schemaName);
	}

	[Test]
	public async Task lookup_schema_name_not_found(CancellationToken cancellationToken) {
		var response = async () => await Client.LookupSchemaNameAsync(
			new LookupSchemaNameRequest {
				SchemaVersionId = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken
		);

		var exception = await response.ShouldThrowAsync<RpcException>();
		exception.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
