// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf.WellKnownTypes;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Domain;

public class UpdateSchemaCommandTests : SchemaApplicationTestFixture {
	[Test]
	public async Task updates_schema_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var originalDescription = Faker.Lorem.Sentence();
		var newDescription = Faker.Lorem.Sentence();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchemaDefinition().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None,
					Description = originalDescription
				}
			},
			cancellationToken
		);

		var expectedEvent = new SchemaDescriptionUpdated {
			SchemaName = schemaName,
			Description = newDescription,
			UpdatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		// Act
		var result = await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Description = newDescription },
				UpdateMask = new FieldMask { Paths = { "Details.Description" } }
			},
			cancellationToken
		);

		// Assert
		var descriptionUpdated = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaDescriptionUpdated>();
		descriptionUpdated.Should().BeEquivalentTo(expectedEvent, o => o.Excluding(e => e.UpdatedAt));
	}

	[Test]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchemaDefinition().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		await Apply(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken);

		// Act
		var updateSchema = async () => await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Description = Faker.Lorem.Sentence() },
				UpdateMask = new FieldMask { Paths = { "Details.Description" } }
			},
			cancellationToken
		);

		// Assert
		await updateSchema.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test]
	public async Task throws_exception_when_update_mask_contains_unknown_field(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchemaDefinition().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var updateSchema = async () => await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Description = Faker.Lorem.Sentence() },
				UpdateMask = new FieldMask { Paths = { "Details.UnknownField" } }
			},
			cancellationToken
		);

		// Assert
		await updateSchema.ShouldThrowAsync<DomainExceptions.EntityException>()
			.WithMessage("Unknown field Details.UnknownField in update mask");
	}

	[Test, NotModifiableTestCases]
	public async Task throws_exception_when_trying_to_update_non_modifiable_fields(
		SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchemaDefinition().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var updateSchema = async () => await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = schemaDetails,
				UpdateMask = new FieldMask { Paths = { maskPath } }
			},
			cancellationToken
		);

		// Assert
		await updateSchema.ShouldThrowAsync<DomainExceptions.EntityNotModified>()
			.WithMessage($"*{errorMessage}*");
	}

	[Test, UnchangedFieldsTestCases]
	public async Task throws_exception_when_fields_has_not_changed(
		SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchemaDefinition().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None,
					Description = schemaDetails.Description,
					Tags = { schemaDetails.Tags }
				}
			},
			cancellationToken
		);

		// Act
		var updateSchema = async () => await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = schemaDetails,
				UpdateMask = new FieldMask { Paths = { maskPath } }
			},
			cancellationToken
		);

		// Assert
		await updateSchema.ShouldThrowAsync<DomainExceptions.EntityNotModified>()
			.WithMessage($"*{errorMessage}*");
	}

	public class NotModifiableTestCases : TestCaseGenerator<SchemaDetails, string, string> {
		protected override IEnumerable<(SchemaDetails, string, string)> Data() {
			yield return (
				new SchemaDetails { Compatibility = CompatibilityMode.Forward },
				"Details.Compatibility",
				"Compatibility mode is not modifiable"
			);
			yield return (
				new SchemaDetails { DataFormat = SchemaFormat.Protobuf },
				"Details.DataFormat",
				"DataFormat is not modifiable"
			);
		}
	}

	public class UnchangedFieldsTestCases : TestCaseGenerator<SchemaDetails, string, string> {
		protected override IEnumerable<(SchemaDetails, string, string)> Data() {
			yield return (
				new SchemaDetails { Description = "Unchanged description" },
				"Details.Description",
				"Description has not changed"
			);
			yield return (
				new SchemaDetails { Tags = { ["env"] = "test" } },
				"Details.Tags",
				"Tags have not changed"
			);
		}
	}
}
