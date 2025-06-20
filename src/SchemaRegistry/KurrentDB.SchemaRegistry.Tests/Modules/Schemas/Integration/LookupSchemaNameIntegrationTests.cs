// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;


public class LookupSchemaNameIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task lookup_schema_name(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var details = new SchemaDetails {
			Description = Faker.Lorem.Word(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = CompatibilityMode.Backward,
			Tags = {  }
		};

		// Act
		var result = await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = details,
			},
			cancellationToken: cancellationToken
		);

		result.Should().NotBeNull();
		result.SchemaVersionId.Should().NotBeEmpty();

		// Assert
		await Wait.UntilAsserted(async () => {
			var lookupSchemaNameResponse = await Client.LookupSchemaNameAsync(
				new LookupSchemaNameRequest {
					SchemaVersionId = result.SchemaVersionId,
				},
				cancellationToken: cancellationToken
			);

			lookupSchemaNameResponse.Should().NotBeNull();
			lookupSchemaNameResponse.SchemaName.Should().Be(schemaName);
		}, cancellationToken: cancellationToken);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task lookup_schema_name_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.LookupSchemaNameAsync(
			new LookupSchemaNameRequest {
				SchemaVersionId = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
