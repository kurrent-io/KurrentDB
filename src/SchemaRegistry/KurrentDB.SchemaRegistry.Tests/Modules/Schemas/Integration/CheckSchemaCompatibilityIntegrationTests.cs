// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class CheckSchemaCompatibilityIntegrationTests : SchemaApplicationTestFixture {
	private const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_matches(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();

		// Arrange
		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = v1.ToByteString(),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Forward,
				Description = Faker.Lorem.Text(),
			},
		}, cancellationToken: cancellationToken);

		var checkResponse = await Client.CheckSchemaCompatibilityAsync(new CheckSchemaCompatibilityRequest {
			SchemaName = schemaName,
			DataFormat = SchemaDataFormat.Json,
			Definition = v1.ToByteString()
		}, cancellationToken: cancellationToken);

		checkResponse.ValidationResult.IsCompatible.Should().BeTrue();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_schema_name_not_found(CancellationToken cancellationToken) {
		var v1 = NewJsonSchemaDefinition();

		var ex = await FluentActions.Awaiting(async () => await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = Guid.NewGuid().ToString(),
				DataFormat = SchemaDataFormat.Json,
				Definition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_schema_version_id_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaVersionId = Guid.NewGuid().ToString(),
				DataFormat = SchemaDataFormat.Json,
				Definition = NewJsonSchemaDefinition().ToByteString()
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_backward_all_is_compatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.RemoveOptional("name");
		var v3 = v2.AddOptional("age", JsonObjectType.String);

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.BackwardAll,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);


		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString(),
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v3.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		response.ValidationResult.IsCompatible.Should().BeTrue();
		response.ValidationResult.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task check_schema_compatibility_backward_all_is_incompatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition()
			.AddOptional("gender", JsonObjectType.String)
			.AddOptional("email", JsonObjectType.String);

		var v2 = v1
			.AddRequired("age", JsonObjectType.Integer)
			.SetRequired("email")
			.SetType("gender", JsonObjectType.Integer);

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.BackwardAll,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		response.ValidationResult.IsCompatible.Should().BeFalse();
		response.ValidationResult.Errors.Should().NotBeEmpty();
		response.ValidationResult.Errors.Count.Should().Be(3);
		response.ValidationResult.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		response.ValidationResult.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		response.ValidationResult.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_backward_is_compatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("address", JsonObjectType.String);

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.Backward,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		response.ValidationResult.IsCompatible.Should().BeTrue();
		response.ValidationResult.Errors.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_backward_is_incompatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddRequired("email", JsonObjectType.String);

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.Backward,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		response.ValidationResult.IsCompatible.Should().BeFalse();
		response.ValidationResult.Errors.Should().NotBeEmpty();
		response.ValidationResult.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_forward_is_compatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition()
			.AddOptional("email", JsonObjectType.String)
			.AddOptional("phone", JsonObjectType.String);

		var v2 = v1.RemoveOptional("phone");

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.Forward,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		response.ValidationResult.IsCompatible.Should().BeTrue();
		response.ValidationResult.Errors.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_forward_is_incompatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.SetType("id", JsonObjectType.Integer);

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.Forward,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		response.ValidationResult.IsCompatible.Should().BeFalse();
		response.ValidationResult.Errors.Should().NotBeEmpty();
		response.ValidationResult.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_full_is_compatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.Full,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		response.ValidationResult.IsCompatible.Should().BeTrue();
		response.ValidationResult.Errors.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_full_is_incompatible(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddRequired("email", JsonObjectType.String);

		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.Full,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Act
		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		response.ValidationResult.IsCompatible.Should().BeFalse();
		response.ValidationResult.Errors.Should().NotBeEmpty();
		response.ValidationResult.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}
}
