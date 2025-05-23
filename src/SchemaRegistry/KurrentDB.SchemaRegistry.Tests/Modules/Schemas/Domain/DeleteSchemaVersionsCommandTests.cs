// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Domain;

public class DeleteSchemaVersionsCommandTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_schema_versions_successfully_in_none_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var initialSchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema with compatibility mode None
		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = initialSchemaDefinition,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken
		);

		// Register additional schema versions
		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		var version3Result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		var versionsToDelete = new List<int> { 1, 2 };

		var expectedEvent = new SchemaVersionsDeleted {
			SchemaName = schemaName,
			LatestSchemaVersionId = version3Result.Changes.GetSingleEvent<SchemaVersionRegistered>().SchemaVersionId,
			LatestSchemaVersionNumber = 3,
			DeletedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		// Act
		var result = await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { versionsToDelete }
			},
			cancellationToken
		);

		// Assert
		var versionsDeleted = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionsDeleted>();
		versionsDeleted.SchemaName.Should().Be(schemaName);
		versionsDeleted.Versions.Should().HaveCount(2);
		versionsDeleted.LatestSchemaVersionId.Should().Be(expectedEvent.LatestSchemaVersionId);
		versionsDeleted.LatestSchemaVersionNumber.Should().Be(expectedEvent.LatestSchemaVersionNumber);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_nonexistent_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create initial schema with compatibility mode None
		await Apply(
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
			cancellationToken
		);

		// Register one additional schema version
		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		var nonExistentVersions = new List<int> { 3, 4 }; // These versions don't exist

		// Act
		var deleteVersions = async () => await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { nonExistentVersions }
			},
			cancellationToken
		);

		// Assert
		await deleteVersions.ShouldThrowAsync<DomainExceptions.EntityException>()
			.WithMessage($"*Schema {schemaName} does not have versions: 3, 4*");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_all_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create initial schema with compatibility mode None
		await Apply(
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
			cancellationToken
		);

		// Act
		var deleteVersions = async () => await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 } // Trying to delete the only version
			},
			cancellationToken
		);

		// Assert
		await deleteVersions.ShouldThrowAsync<DomainExceptions.EntityException>()
			.WithMessage($"*Cannot delete all versions of schema {schemaName}*");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_latest_version_in_backward_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create initial schema with backward compatibility mode
		await Apply(
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
			cancellationToken
		);

		// Register additional schema version
		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		// Act
		var deleteLatestVersion = async () => await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 2 } // Trying to delete the latest version
			},
			cancellationToken
		);

		// Assert
		await deleteLatestVersion.ShouldThrowAsync<DomainExceptions.EntityException>()
			.WithMessage($"*Cannot delete the latest version of schema {schemaName} in Backward compatibility mode*");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_older_version_successfully_in_backward_compatibility_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create initial schema with backward compatibility mode
		await Apply(
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
			cancellationToken
		);

		// Register additional schema version
		var version2Result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		var latestVersionId = version2Result.Changes.GetSingleEvent<SchemaVersionRegistered>().SchemaVersionId;

		// Act - delete the older version (1) while keeping the latest (2)
		var result = await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken
		);

		// Assert
		var versionsDeleted = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionsDeleted>();
		versionsDeleted.SchemaName.Should().Be(schemaName);
		versionsDeleted.LatestSchemaVersionId.Should().Be(latestVersionId);
		versionsDeleted.LatestSchemaVersionNumber.Should().Be(2);
	}

	[Test]
	[Arguments(CompatibilityMode.Forward)]
	[Arguments(CompatibilityMode.Full)]
	[Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_delete_any_version_in_forward_or_full_compatibility_mode(
		CompatibilityMode compatibilityMode, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create initial schema with specified compatibility mode
		await Apply(
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
			cancellationToken
		);

		// Register additional schema version
		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		// Act - attempt to delete any version
		var deleteVersion = async () => await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 } // Trying to delete first version
			},
			cancellationToken
		);

		// Assert
		await deleteVersion.ShouldThrowAsync<DomainExceptions.EntityException>()
			.WithMessage($"*Cannot delete versions of schema {schemaName} in {compatibilityMode} compatibility mode*");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create schema
		await Apply(
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
			cancellationToken
		);

		// Register additional version
		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		// Delete the schema
		await Apply(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken);

		// Act
		var deleteVersions = async () => await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken
		);

		// Assert
		await deleteVersions.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_does_not_exist(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Act
		var deleteVersions = async () => await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = nonExistentSchemaName,
				Versions = { 1 }
			},
			cancellationToken
		);

		// Assert
		await deleteVersions.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}
}
