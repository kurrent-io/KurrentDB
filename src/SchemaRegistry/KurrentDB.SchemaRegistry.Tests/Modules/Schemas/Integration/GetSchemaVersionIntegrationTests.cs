// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class GetSchemaVersionIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_version(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();

		var details = new SchemaDetails {
			Description = Faker.Lorem.Word(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = CompatibilityMode.Backward
		};

		// Act
		var result = await CreateSchema(schemaName, v1, details, cancellationToken);

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

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_version_by_id(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();

		var details = new SchemaDetails {
			Description = Faker.Lorem.Word(),
			DataFormat = SchemaDataFormat.Json,
			Compatibility = CompatibilityMode.Backward
		};

		// Act
		var result = await CreateSchema(schemaName, v1, details, cancellationToken);

		// Assert
		var getSchemaResponse = await Client.GetSchemaVersionByIdAsync(
			new GetSchemaVersionByIdRequest {
				SchemaVersionId = result.SchemaVersionId
			},
			cancellationToken: cancellationToken
		);

		getSchemaResponse.Should().NotBeNull();
		getSchemaResponse.Version.SchemaVersionId.Should().Be(result.SchemaVersionId);
		getSchemaResponse.Version.VersionNumber.Should().Be(result.VersionNumber);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_version_with_stream_name_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.GetSchemaVersionAsync(
			new GetSchemaVersionRequest {
				SchemaName = Guid.NewGuid().ToString(),
				VersionNumber = 1,
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_version_with_version_id_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.GetSchemaVersionByIdAsync(
			new GetSchemaVersionByIdRequest {
				SchemaVersionId = Guid.NewGuid().ToString()
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
