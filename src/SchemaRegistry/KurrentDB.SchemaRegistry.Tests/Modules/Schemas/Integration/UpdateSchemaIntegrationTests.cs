// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;


public class UpdateSchemaIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task updates_schema_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var originalDescription = Faker.Lorem.Sentence();
		var newDescription = Faker.Lorem.Sentence();
		var originalTags = new Dictionary<string, string> { ["env"] = "test" };
		var newTags = new Dictionary<string, string> { ["env"] = "prod" };

		await CreateSchemaAsync(schemaName: schemaName, description: originalDescription, tags: originalTags, cancellationToken: cancellationToken);

		// Act
		var updateSchemaResult = await Client.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					Description = newDescription,
					Compatibility = CompatibilityMode.Backward,
					DataFormat = SchemaFormat.Json,
					Tags = { newTags }
				},
				UpdateMask = new FieldMask { Paths = { "Details.Description", "Details.Tags" } }
			},
			cancellationToken: cancellationToken
		);

		var listSchemasResult = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = schemaName
			},
			cancellationToken: cancellationToken
		);

		// Assert
		updateSchemaResult.Should().NotBeNull();
		listSchemasResult.Schemas.Should().HaveCount(1);
		listSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listSchemasResult.Schemas.First().Details.Description.Should().Be(newDescription);
		listSchemasResult.Schemas.First().Details.Tags.Should().BeEquivalentTo(newTags);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		await Client.DeleteSchemaAsync(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken: cancellationToken);

		// Act
		var updateSchema = async () => await Client.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					Compatibility = CompatibilityMode.Backward,
					DataFormat = SchemaFormat.Json
				},
				UpdateMask = new FieldMask { Paths = { "Details.Description" } }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
		updateSchemaException.Which.Message.Should().Contain($"Schema schemas/{schemaName} not found");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_update_mask_contains_unknown_field(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Act
		var updateSchema = async () => await Client.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					Compatibility = CompatibilityMode.Backward,
					DataFormat = SchemaFormat.Json
				},
				UpdateMask = new FieldMask { Paths = { "Details.UnknownField" } }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		updateSchemaException.Which.Message.Should().Contain("Unknown field Details.UnknownField in update mask");
	}

	[Test, NotModifiableTestCases]
	[Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_trying_to_update_non_modifiable_fields(
		SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Act
		var updateSchema = async () => await Client.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = schemaDetails,
				UpdateMask = new FieldMask { Paths = { maskPath } }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		updateSchemaException.Which.Message.Should().Contain(errorMessage);
	}

	[Test, UnchangedFieldsTestCases]
	[Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_fields_has_not_changed(
		SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken,
			description: schemaDetails.Description,
			tags: new Dictionary<string, string>(schemaDetails.Tags)
		);

		// Act
		var updateSchema = async () => await Client.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = schemaDetails,
				UpdateMask = new FieldMask { Paths = { maskPath } }
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		updateSchemaException.Which.Message.Should().Contain(errorMessage);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task handles_case_insensitive_field_paths(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDescription = Faker.Lorem.Sentence();

		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Act
		var updateSchemaResult = await Client.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					Description = newDescription,
					Compatibility = CompatibilityMode.Backward,
					DataFormat = SchemaFormat.Json
				},
				UpdateMask = new FieldMask { Paths = { "details.description" } }
			},
			cancellationToken: cancellationToken
		);

		var listSchemasResult = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = schemaName
			},
			cancellationToken: cancellationToken
		);

		// Assert
		updateSchemaResult.Should().NotBeNull();

		listSchemasResult.Schemas.Should().HaveCount(1);
		listSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listSchemasResult.Schemas.First().Details.Description.Should().Be(newDescription);
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
