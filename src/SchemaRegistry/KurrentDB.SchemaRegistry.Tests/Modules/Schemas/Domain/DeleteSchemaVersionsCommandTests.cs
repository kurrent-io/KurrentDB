// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf.WellKnownTypes;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Domain;

public class DeleteSchemaVersionsCommandTests : SchemaApplicationTestFixture {
	[Test]
	[Arguments(CompatibilityMode.None)]
	[Arguments(CompatibilityMode.Backward)]
	public async Task delete_versions_successfully(CompatibilityMode compatibility, CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);
		var v3 = v2.AddOptional("age", JsonObjectType.Integer);

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = v1.ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = compatibility,
				}
			},
			cancellationToken
		);

		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken
		);

		var version3Result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v3.ToByteString()
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

	[Test]
	public async Task throws_not_found_for_non_existing_schema(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = v1.ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.None,
				}
			},
			cancellationToken
		);

		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken
		);

		await Apply(
			new DeleteSchemaRequest {
				SchemaName = schemaName
			},
			cancellationToken
		);

		// Act & Assert
		var action = async () =>
			await Apply(
				new DeleteSchemaVersionsRequest {
					SchemaName = schemaName,
					Versions = { 1 }
				},
				cancellationToken
			);

		await action.Should().ThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test]
	public async Task throws_precondition_when_deleting_all_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = v1.ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.None,
				}
			},
			cancellationToken
		);

		// Act & Assert
		var action = async () =>
			await Apply(
				new DeleteSchemaVersionsRequest {
					SchemaName = schemaName,
					Versions = { 1 }
				},
				cancellationToken
			);

		var exception = await action.Should().ThrowAsync<DomainExceptions.EntityException>();
		exception.WithMessage($"*Cannot delete all versions of schema {schemaName}*");
	}

	[Test]
	[Arguments(CompatibilityMode.Forward)]
	[Arguments(CompatibilityMode.Full)]
	public async Task throws_precondition_for_restricted_compatibility_modes(
		CompatibilityMode compatibility, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = v1.ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = compatibility
				}
			},
			cancellationToken
		);

		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken
		);

		// Act & Assert
		var action = async () =>
			await Apply(
				new DeleteSchemaVersionsRequest {
					SchemaName = schemaName,
					Versions = { 1 }
				},
				cancellationToken
			);

		var exception = await action.Should().ThrowAsync<DomainExceptions.EntityException>();
		exception.WithMessage($"*Cannot delete versions of schema {schemaName} in {compatibility} compatibility mode*");
	}

	[Test]
	public async Task throws_precondition_when_deleting_latest_in_backward_mode(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = v1.ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.Backward
				}
			},
			cancellationToken
		);

		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken
		);

		// Act & Assert
		var action = async () =>
			await Apply(
				new DeleteSchemaVersionsRequest {
					SchemaName = schemaName,
					Versions = { 2 }
				},
				cancellationToken
			);

		var exception = await action.Should().ThrowAsync<DomainExceptions.EntityException>();
		exception.WithMessage($"*Cannot delete the latest version of schema {schemaName} in Backward compatibility mode*");
	}

	[Test]
	public async Task throws_precondition_for_non_existing_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = v1.ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken
		);

		var nonExistentVersions = new List<int> { 3, 4 };

		// Act & Assert
		var action = async () =>
			await Apply(
				new DeleteSchemaVersionsRequest {
					SchemaName = schemaName,
					Versions = { nonExistentVersions }
				},
				cancellationToken
			);

		var exception = await action.Should().ThrowAsync<DomainExceptions.EntityException>();
		exception.WithMessage($"*Schema {schemaName} does not have versions: 3, 4*");
	}
}
