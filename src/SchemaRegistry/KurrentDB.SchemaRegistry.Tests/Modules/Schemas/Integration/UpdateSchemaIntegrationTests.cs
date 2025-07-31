// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using Shouldly;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class UpdateSchemaIntegrationTests : SchemaApplicationTestFixture {
	[Test]
	public async Task updates_schema_successfully(CancellationToken cancellationToken) {
		// Arrange
		var prefix = NewPrefix();
		var schemaName = NewSchemaName(prefix);
		var originalTags = new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() };
		var newTags = new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() };

		await CreateSchema(
			schemaName,
			new SchemaDetails {
				Tags = { originalTags },
				Compatibility = CompatibilityMode.Forward,
				DataFormat = SchemaFormat.Json
			},
			cancellationToken
		);

		// Act
		await UpdateSchema(
			schemaName,
			new SchemaDetails {
				Tags = { newTags },
				Compatibility = CompatibilityMode.Forward,
				DataFormat = SchemaFormat.Json
			},
			new FieldMask { Paths = { "Details.Tags" } },
			cancellationToken
		);

		// Assert
		var listSchemasResult = await ListRegisteredSchemas(prefix, cancellationToken);

		listSchemasResult
			.Schemas.ShouldHaveSingleItem()
			.Tags.ShouldBe(newTags);
	}

	[Test]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await CreateSchema(schemaName, cancellationToken);
		await DeleteSchema(schemaName, cancellationToken);

		// Act
		var updateSchema = async () => await UpdateSchema(
			schemaName,
			new SchemaDetails {
				Description = Faker.Lorem.Sentence(),
				Compatibility = CompatibilityMode.Backward,
				DataFormat = SchemaFormat.Json
			},
			new FieldMask { Paths = { "Details.Description" } },
			cancellationToken
		);

		// Assert
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
		updateSchemaException.Which.Message.Should().Contain($"Schema schemas/{schemaName} not found");
	}

	[Test]
	public async Task throws_exception_when_update_mask_contains_unknown_field(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await CreateSchema(schemaName: schemaName, cancellationToken: cancellationToken);

		// Act
		var updateSchema = async () => await UpdateSchema(
			schemaName,
			new SchemaDetails {
				Description = Faker.Lorem.Sentence(),
				Compatibility = CompatibilityMode.Backward,
				DataFormat = SchemaFormat.Json
			},
			new FieldMask { Paths = { "Details.UnknownField" } },
			cancellationToken
		);

		// Assert
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		updateSchemaException.Which.Message.Should().Contain("Unknown field Details.UnknownField in update mask");
	}

	[Test, NotModifiableTestCases]
	public async Task throws_exception_when_trying_to_update_non_modifiable_fields(
		SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchema(schemaName, cancellationToken);

		// Act
		var updateSchema = async () => await UpdateSchema(
			schemaName,
			schemaDetails,
			new FieldMask { Paths = { maskPath } },
			cancellationToken
		);

		// Assert
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		updateSchemaException.Which.Message.Should().Contain(errorMessage);
	}

	[Test, UnchangedFieldsTestCases]
	public async Task throws_exception_when_fields_has_not_changed(SchemaDetails schemaDetails, string maskPath, string errorMessage,
		CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var details = new SchemaDetails {
			Description = schemaDetails.Description,
			Compatibility = CompatibilityMode.None,
			DataFormat = SchemaFormat.Json,
			Tags = { new Dictionary<string, string>(schemaDetails.Tags) }
		};

		await CreateSchema(schemaName, details, cancellationToken);

		// Act
		var updateSchema = async () => await UpdateSchema(
			schemaName,
			schemaDetails,
			new FieldMask { Paths = { maskPath } },
			cancellationToken
		);

		// Assert
		var exception = await updateSchema.Should().ThrowAsync<RpcException>();
		exception.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		exception.Which.Message.Should().Contain(errorMessage);
	}

	public class NotModifiableTestCases : TestCaseGenerator<SchemaDetails, string, string> {
		protected override IEnumerable<(SchemaDetails, string, string)> Data() {
			yield return (
				new SchemaDetails {
					Compatibility = CompatibilityMode.Forward,
					DataFormat = SchemaFormat.Json
				},
				"Details.Compatibility",
				"Compatibility mode is not modifiable"
			);
			yield return (
				new SchemaDetails {
					Compatibility = CompatibilityMode.Forward,
					DataFormat = SchemaFormat.Protobuf
				},
				"Details.DataFormat",
				"DataFormat is not modifiable"
			);
		}
	}

	public class UnchangedFieldsTestCases : TestCaseGenerator<SchemaDetails, string, string> {
		protected override IEnumerable<(SchemaDetails, string, string)> Data() {
			yield return (
				new SchemaDetails {
					Description = "Unchanged description",
					Compatibility = CompatibilityMode.Backward,
					DataFormat = SchemaFormat.Json
				},
				"Details.Description",
				"Description has not changed"
			);
			yield return (
				new SchemaDetails {
					Tags = { ["env"] = "test" },
					Compatibility = CompatibilityMode.Backward,
					DataFormat = SchemaFormat.Json
				},
				"Details.Tags",
				"Tags have not changed"
			);
		}
	}
}
