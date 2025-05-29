// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Elastic.Clients.Elasticsearch.Nodes;
using Google.Protobuf;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class GetSchemaVersionIntegrationTests : SchemaApplicationTestFixture {

	private const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_version(CancellationToken cancellationToken) {
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

		// Assert
		var getSchemaResponse = await Client.GetSchemaVersionAsync(
			new GetSchemaVersionRequest {
				SchemaName = schemaName,
				VersionNumber = result.VersionNumber
			},
			cancellationToken: cancellationToken
		);

		getSchemaResponse.Should().NotBeNull();
		getSchemaResponse.Version.SchemaVersionId.Should().Be(result.SchemaVersionId);
		getSchemaResponse.Version.VersionNumber.Should().Be(result.VersionNumber);
	}
}
