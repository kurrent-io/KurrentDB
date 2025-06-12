// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

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

public class UpdateSchemaCommandTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task updates_schema_description_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var originalDescription = Faker.Lorem.Sentence();
		var newDescription = Faker.Lorem.Sentence();

		// Create initial schema
		await Apply(
			CreateSchemaRequest(schemaName: schemaName, description: originalDescription),
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

	[Test, Timeout(TestTimeoutMs)]
	public async Task updates_schema_tags_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var originalTags = new Dictionary<string, string> { ["env"] = "test", ["version"] = "1.0" };
		var newTags = new Dictionary<string, string> { ["env"] = "prod", ["team"] = "data" };

		// Create initial schema
		await Apply(
			CreateSchemaRequest(schemaName: schemaName, tags: originalTags),
			cancellationToken
		);

		var expectedEvent = new SchemaTagsUpdated {
			SchemaName = schemaName,
			Tags = { newTags },
			UpdatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		// Act
		var result = await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Tags = { newTags } },
				UpdateMask = new FieldMask { Paths = { "Details.Tags" } }
			},
			cancellationToken
		);

		// Assert
		var tagsUpdated = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaTagsUpdated>();
		tagsUpdated.Should().BeEquivalentTo(expectedEvent, o => o.Excluding(e => e.UpdatedAt));
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Act
		var updateSchema = async () => await Apply(
			new UpdateSchemaRequest {
				SchemaName = nonExistentSchemaName,
				Details = new SchemaDetails { Description = Faker.Lorem.Sentence() },
				UpdateMask = new FieldMask { Paths = { "Details.Description" } }
			},
			cancellationToken
		);

		// Assert
		await updateSchema.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		// Create and then delete schema
		await Apply(
			CreateSchemaRequest(schemaName: schemaName),
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

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_update_mask_is_empty(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		// Create initial schema
		await Apply(
			CreateSchemaRequest(schemaName: schemaName),
			cancellationToken
		);

		// Act
		var updateSchema = async () => await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Description = Faker.Lorem.Sentence() },
				UpdateMask = new FieldMask()
			},
			cancellationToken
		);

		// Assert
		await updateSchema.ShouldThrowAsync<DomainExceptions.EntityException>()
			.WithMessage("Update mask must contain at least one field");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_update_mask_contains_unknown_field(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		// Create initial schema
		await Apply(
			CreateSchemaRequest(schemaName: schemaName),
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
	[Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_update_non_modifiable_fields(
		SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();

		// Create initial schema
		await Apply(
			CreateSchemaRequest(schemaName: schemaName),
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
	[Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_fields_has_not_changed(
		SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();

		// Create initial schema
		await Apply(
			CreateSchemaRequest(
				schemaName: schemaName,
				description: schemaDetails.Description,
				tags: new Dictionary<string, string>(schemaDetails.Tags)
			),
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

	[Test, Timeout(TestTimeoutMs)]
	public async Task handles_case_insensitive_field_paths(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDescription = Faker.Lorem.Sentence();

		// Create initial schema
		await Apply(
			CreateSchemaRequest(schemaName: schemaName),
			cancellationToken
		);

		// Act
		var result = await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Description = newDescription },
				UpdateMask = new FieldMask { Paths = { "details.description" } }
			},
			cancellationToken
		);

		// Assert
		var descriptionUpdated = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaDescriptionUpdated>();
		descriptionUpdated.Description.Should().Be(newDescription);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task updates_empty_tags_to_non_empty_tags(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var newTags = new Dictionary<string, string> { ["env"] = "prod", ["team"] = "backend" };

		// Create initial schema with no tags
		await Apply(
			CreateSchemaRequest(schemaName: schemaName, tags: null),
			cancellationToken
		);

		// Act
		var result = await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Tags = { newTags } },
				UpdateMask = new FieldMask { Paths = { "Details.Tags" } }
			},
			cancellationToken
		);

		// Assert
		var tagsUpdated = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaTagsUpdated>();
		tagsUpdated.Tags.Should().BeEquivalentTo(newTags);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task updates_non_empty_tags_to_empty_tags(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var initialTags = new Dictionary<string, string> { ["env"] = "test", ["version"] = "1.0" };

		// Create initial schema with tags
		await Apply(
			CreateSchemaRequest(schemaName: schemaName, tags: initialTags),
			cancellationToken
		);

		// Act
		var result = await Apply(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails { Tags = { } },
				UpdateMask = new FieldMask { Paths = { "Details.Tags" } }
			},
			cancellationToken
		);

		// Assert
		var tagsUpdated = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaTagsUpdated>();
		tagsUpdated.Tags.Should().BeEmpty();
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
