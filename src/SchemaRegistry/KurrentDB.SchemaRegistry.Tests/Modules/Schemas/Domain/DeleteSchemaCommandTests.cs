// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
	[Test, Timeout(20_000)]
	public async Task deletes_schema_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create initial schema
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

	[Test, Timeout(20_000)]
	public async Task deletes_schema_with_multiple_versions_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create initial schema
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

		// Register additional versions
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

	[Test, Timeout(20_000)]
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

	[Test, Timeout(20_000)]
	public async Task throws_exception_when_schema_is_already_deleted(CancellationToken cancellationToken) {
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
					Compatibility = CompatibilityMode.Backward,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken
		);

		// Delete schema first time
		await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Act - Try to delete again
		var deleteSchema = async () => await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Assert
		await deleteSchema.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test, CompatibilityModeTestCases]
	[Timeout(20_000)]
	public async Task deletes_schema_with_different_compatibility_modes(CompatibilityMode compatibilityMode, CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{compatibilityMode}-{Identifiers.GenerateShortId()}";

		// Create schema with specific compatibility mode
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

		// Act
		var result = await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Assert
		var schemaDeleted = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaDeleted>();
		schemaDeleted.SchemaName.Should().Be(schemaName);
	}


	[Test, DataFormatTestCases]
	[Timeout(20_000)]
	public async Task deletes_schema_with_different_data_formats(SchemaFormat dataFormat, CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{dataFormat}-{Identifiers.GenerateShortId()}";

		// Create schema with specific data format
		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = dataFormat,
					Compatibility = CompatibilityMode.Backward,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken
		);

		// Act
		var result = await Apply(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken
		);

		// Assert
		var schemaDeleted = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaDeleted>();
		schemaDeleted.SchemaName.Should().Be(schemaName);
	}

	[Test, Timeout(20_000)]
	public async Task deletes_schema_with_complex_tags(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var complexTags = new Dictionary<string, string> {
			["environment"] = "production",
			["team"] = "data-engineering",
			["version"] = "2.1.0",
			["owner"] = "john.doe@company.com",
			["criticality"] = "high",
			["region"] = "us-east-1"
		};

		// Create schema with complex tags
		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward,
					Tags = { complexTags }
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

	[Test, Timeout(20_000)]
	public async Task deletes_schema_with_empty_description_and_no_tags(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create minimal schema
		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward
					// No description or tags
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

	[Test, Timeout(20_000)]
	public async Task subsequent_operations_on_deleted_schema_throw_exceptions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Create and delete schema
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

		// Act & Assert - Try various operations on deleted schema
		var updateSchema = async () => await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Description = Faker.Lorem.Sentence() },
				UpdateMask = new FieldMask { Paths = { "Details.Description" } }
			},
			cancellationToken
		);
		await updateSchema.ShouldThrowAsync<DomainExceptions.EntityNotFound>();

		var registerVersion = async () => await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken
		);
		await registerVersion.ShouldThrowAsync<DomainExceptions.EntityNotFound>();

		var deleteVersions = async () => await Apply(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken
		);
		await deleteVersions.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	public class CompatibilityModeTestCases : TestCaseGenerator<CompatibilityMode> {
		protected override IEnumerable<CompatibilityMode> Data() {
			yield return CompatibilityMode.Backward;
			yield return CompatibilityMode.Forward;
			yield return CompatibilityMode.Full;
			yield return CompatibilityMode.None;
		}
	}

	public class DataFormatTestCases : TestCaseGenerator<SchemaFormat> {
		protected override IEnumerable<SchemaFormat> Data() {
			yield return SchemaFormat.Json;
			yield return SchemaFormat.Protobuf;
			yield return SchemaFormat.Avro;
			yield return SchemaFormat.Bytes;
		}
	}
}
