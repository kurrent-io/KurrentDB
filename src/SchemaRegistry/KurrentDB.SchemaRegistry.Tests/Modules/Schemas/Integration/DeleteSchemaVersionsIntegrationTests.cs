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
	[Test, Skip("True")]
	[Arguments(CompatibilityMode.None)]
	[Arguments(CompatibilityMode.Backward)]
	public async Task delete_versions_successfully(CompatibilityMode compatibility, CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchema();
		var v2 = v1.AddOptional("email", JsonObjectType.String);
		var v3 = v2.AddOptional("age", JsonObjectType.Integer);

		await CreateSchema(schemaName, v1, compatibility, SchemaDataFormat.Json, cancellationToken);
		await RegisterSchemaVersion(schemaName, v2, cancellationToken);
		await RegisterSchemaVersion(schemaName, v3, cancellationToken);

		// Act
		var schemaVersionsResult = await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { new List<int> { 1, 2 } }
			},
			cancellationToken: cancellationToken
		);

		var listSchemaVersionsResult = await ListSchemaVersions(schemaName, cancellationToken);

		// Assert
		schemaVersionsResult.Should().NotBeNull();
		listSchemaVersionsResult.Versions.Should().NotBeEmpty();
		listSchemaVersionsResult.Versions.Should().ContainSingle();
		listSchemaVersionsResult.Versions.First().VersionNumber.Should().Be(3);
	}

	[Test]
	public async Task throws_not_found_for_non_existing_schema(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchema();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchema(schemaName, v1, cancellationToken);
		await RegisterSchemaVersion(schemaName, v2, cancellationToken);
		await DeleteSchema(schemaName, cancellationToken);

		// Act
		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test]
	public async Task throws_precondition_when_deleting_all_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await CreateSchema(schemaName, cancellationToken);

		// Act
		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionsException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionsException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteVersionsException.Which.Message.Should().Contain($"Cannot delete all versions of schema {schemaName}");
	}

	[Test]
	[Arguments(CompatibilityMode.Forward)]
	[Arguments(CompatibilityMode.Full)]
	public async Task throws_precondition_for_restricted_compatibility_modes(
		CompatibilityMode compatibilityMode, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchema();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchema(schemaName, v1, compatibilityMode, SchemaDataFormat.Json, cancellationToken);
		await RegisterSchemaVersion(schemaName, v2, cancellationToken);

		// Act
		var deleteVersion = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionException = await deleteVersion.Should().ThrowAsync<RpcException>();
		deleteVersionException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteVersionException.Which.Message.Should().Contain($"Cannot delete versions of schema {schemaName} in {compatibilityMode} compatibility mode");
	}

	[Test]
	public async Task throws_precondition_when_deleting_latest_in_backward_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchema();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchema(schemaName, v1, CompatibilityMode.Backward, SchemaDataFormat.Json, cancellationToken);
		await RegisterSchemaVersion(schemaName, v2, cancellationToken);

		// Act
		var deleteLatestVersion = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 2 }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteLatestVersionException = await deleteLatestVersion.Should().ThrowAsync<RpcException>();
		deleteLatestVersionException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteLatestVersionException.Which.Message.Should().Contain($"Cannot delete the latest version of schema {schemaName} in Backward compatibility mode");
	}

	[Test]
	public async Task throws_precondition_for_non_existing_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchema();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchema(schemaName, v1, cancellationToken);
		await RegisterSchemaVersion(schemaName, v2, cancellationToken);

		var nonExistentVersions = new List<int> { 3, 4 };

		// Act
		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { nonExistentVersions }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionsException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionsException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteVersionsException.Which.Message.Should().Contain($"Schema {schemaName} does not have versions: 3, 4");
	}
}
