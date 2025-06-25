// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using NJsonSchema;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class DeleteSchemaVersionsIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_all_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();

		await CreateSchema(schemaName, v1, cancellationToken);

		// Act & Assert
		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken: cancellationToken
		);

		var deleteVersionsException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionsException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteVersionsException.Which.Message.Should().Contain($"Cannot delete all versions of schema {schemaName}");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_deleting_latest_version_in_backward_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchema(
			schemaName, v1,
			new SchemaDetails { DataFormat = SchemaDataFormat.Json, Compatibility = CompatibilityMode.Backward },
			cancellationToken
		);
		await RegisterSchemaVersion(schemaName, v2, cancellationToken);

		// Act
		var deleteVersions = async () => await DeleteSchemaVersions(schemaName, [2], cancellationToken);

		// Assert
		var exception = await deleteVersions.Should().ThrowAsync<RpcException>();
		exception.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		exception.Which.Message.Should().Contain($"Cannot delete the latest version of schema {schemaName} in Backward compatibility mode");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_is_not_found(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		// Act
		var deleteVersions = async () => await DeleteSchemaVersions(schemaName, [1], cancellationToken);

		// Assert
		var deleteVersionException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
