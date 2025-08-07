// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Domain;

public class DeleteSchemaCommandTests : SchemaApplicationTestFixture {
	[Test]
	public async Task deletes_schema_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

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

		var expectedEvent = new SchemaDeleted {
			SchemaName = schemaName,
			DeletedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		// Act
		var result = await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Assert
		var schemaDeleted = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaDeleted>();
		schemaDeleted.Should().BeEquivalentTo(expectedEvent, o => o.Excluding(e => e.DeletedAt));
	}

	[Test]
	public async Task deletes_schema_with_multiple_versions_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

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

		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);

		var expectedEvent = new SchemaDeleted {
			SchemaName = schemaName,
			DeletedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		// Act
		var result = await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Assert
		var schemaDeleted = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaDeleted>();
		schemaDeleted.Should().BeEquivalentTo(expectedEvent, o => o.Excluding(e => e.DeletedAt));
	}

	[Test]
	public async Task throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Act
		var deleteSchema = async () => await Apply(
			new DeleteSchemaRequest { SchemaName = nonExistentSchemaName },
			cancellationToken
		);

		// Assert
		await deleteSchema.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test]
	public async Task throws_exception_when_schema_is_already_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

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

		await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Act
		var deleteSchema = async () => await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Assert
		await deleteSchema.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}
}
