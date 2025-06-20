// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using NJsonSchema;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;


public class DeleteSchemaVersionsIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 10_000;

	[Test]
	[Timeout(TestTimeoutMs)]
	public async Task deletes_schema_versions_successfully_in_none_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);
		var v3 = v1.AddOptional("age", JsonObjectType.Integer);

		await CreateSchemaAsync(schemaName: schemaName, schemaDefinition: v1.ToByteString(), cancellationToken: cancellationToken);

		// Register additional schema versions
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v3.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var schemaVersionsResult = await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { new List<int> { 1, 2 } }
			},
			cancellationToken: cancellationToken
		);

		var listRegisteredSchemasResult = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = schemaName
			},
			cancellationToken: cancellationToken
		);

		// Assert
		schemaVersionsResult.Should().NotBeNull();
		listRegisteredSchemasResult.Schemas.Should().NotBeEmpty();
		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(v3.ToByteString());
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_nonexistent_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchemaAsync(schemaName: schemaName, schemaDefinition: v1.ToByteString(), compatibility: CompatibilityMode.None,
			cancellationToken: cancellationToken);

		// Register one additional schema version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		var nonExistentVersions = new List<int> { 3, 4 }; // These versions don't exist

		// Act
		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { nonExistentVersions }
			},
			cancellationToken: cancellationToken
		);

		var listRegisteredSchemasResult = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = schemaName
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionsException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionsException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteVersionsException.Which.Message.Should().Contain($"Schema {schemaName} does not have versions: 3, 4");

		listRegisteredSchemasResult.Schemas.Should().NotBeEmpty();
		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(v2.ToByteString());
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_all_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Act
		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 } // Trying to delete the only version
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionsException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionsException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteVersionsException.Which.Message.Should().Contain($"Cannot delete all versions of schema {schemaName}");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_latest_version_in_backward_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await CreateSchemaAsync(schemaName: schemaName, compatibility: CompatibilityMode.Backward, cancellationToken: cancellationToken);

		// Register additional schema version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken: cancellationToken
		);

		// Act
		var deleteLatestVersion = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 2 } // Trying to delete the latest version
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteLatestVersionException = await deleteLatestVersion.Should().ThrowAsync<RpcException>();
		deleteLatestVersionException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteLatestVersionException.Which.Message.Should().Contain($"Cannot delete the latest version of schema {schemaName} in Backward compatibility mode");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_older_version_successfully_in_backward_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchemaAsync(
			schemaName: schemaName,
			schemaDefinition: v1.ToByteString(),
			cancellationToken: cancellationToken
		);

		// Register additional schema version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act - delete the older version (1) while keeping the latest (2)
		var deleteSchemaVersionsResult = await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken: cancellationToken
		);

		var listRegisteredSchemasResult = await Client.ListRegisteredSchemasAsync(
			new ListRegisteredSchemasRequest {
				SchemaNamePrefix = schemaName
			},
			cancellationToken: cancellationToken
		);

		// Assert
		deleteSchemaVersionsResult.Should().NotBeNull();

		listRegisteredSchemasResult.Schemas.Should().NotBeEmpty();
		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(v2.ToByteString());
	}

	[Test]
	[Arguments(CompatibilityMode.Forward)]
	[Arguments(CompatibilityMode.Full)]
	[Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_any_version_in_forward_or_full_compatibility_mode(
		CompatibilityMode compatibilityMode, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchemaAsync(schemaName: schemaName, compatibility: compatibilityMode, schemaDefinition: v1.ToByteString(),
			cancellationToken: cancellationToken);

		// Register additional schema version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act - attempt to delete any version
		var deleteVersion = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 } // Trying to delete first version
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionException = await deleteVersion.Should().ThrowAsync<RpcException>();
		deleteVersionException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		deleteVersionException.Which.Message.Should().Contain($"Cannot delete versions of schema {schemaName} in {compatibilityMode} compatibility mode");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Register additional version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken: cancellationToken
		);

		// Delete the schema
		await Client.DeleteSchemaAsync(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken: cancellationToken);

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
}
