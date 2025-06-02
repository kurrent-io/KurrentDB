// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class DeleteSchemaVersionsIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_schema_versions_successfully_in_none_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var definition1 = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var definition2 = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var definition3 = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema with compatibility mode None
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = definition1,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

		// Register additional schema versions
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = definition2
			},
			cancellationToken: cancellationToken
		);

		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = definition3
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
		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(definition3);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_nonexistent_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var definition1 = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var definition2 = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema with compatibility mode None
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = definition1,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

		// Register one additional schema version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = definition2
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

		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(definition2);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_all_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		// Create initial schema with compatibility mode None
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

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

		// Create initial schema with backward compatibility mode
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

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

	[Test]
	public async Task deletes_older_version_successfully_in_backward_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var definition1 = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var definition2 = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema with backward compatibility mode
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = definition1,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

		// Register additional schema version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = definition2
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

		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(definition2);
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

		// Create initial schema with specified compatibility mode
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = compatibilityMode,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

		// Register additional schema version
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
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

		// Create schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

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

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_does_not_exist(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Act
		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = nonExistentSchemaName,
				Versions = { 1 }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteVersionException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
