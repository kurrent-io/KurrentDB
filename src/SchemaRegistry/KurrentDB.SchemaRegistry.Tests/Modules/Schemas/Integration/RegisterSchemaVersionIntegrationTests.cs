// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class RegisterSchemaVersionIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task registers_new_schema_version_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var newDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = originalDefinition,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward,
					Tags = { new Dictionary<string, string> { ["env"] = "test" } }
				}
			},
			cancellationToken: cancellationToken
		);

		// Act
		var registerSchemaVersionResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = newDefinition
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
		registerSchemaVersionResult.Should().NotBeNull();
		registerSchemaVersionResult.VersionNumber.Should().Be(2);

		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(newDefinition);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task registers_multiple_schema_versions_with_incrementing_version_numbers(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var secondDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var thirdDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = originalDefinition,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward
				}
			},
			cancellationToken: cancellationToken
		);

		// Act - Register second version
		var secondResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = secondDefinition
			},
			cancellationToken: cancellationToken
		);

		// Act - Register third version
		var thirdResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = thirdDefinition
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
		secondResult.VersionNumber.Should().Be(2);
		thirdResult.VersionNumber.Should().Be(3);

		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().SchemaDefinition.Should().BeEquivalentTo(thirdDefinition);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var schemaDefinition = Faker.Lorem.Text();

		// Act
		var registerVersion = async () => await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = nonExistentSchemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var registerVersionException = await registerVersion.Should().ThrowAsync<RpcException>();
		registerVersionException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = Faker.Lorem.Text();
		var newDefinition = Faker.Lorem.Text();

		// Create and then delete schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(originalDefinition),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward
				}
			},
			cancellationToken: cancellationToken
		);

		await Client.DeleteSchemaAsync(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken: cancellationToken);

		// Act
		var registerVersion = async () => await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var registerVersionException = await registerVersion.Should().ThrowAsync<RpcException>();
		registerVersionException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_definition_has_not_changed(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var schemaDefinition = Faker.Lorem.Text();

		// Create initial schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward
				}
			},
			cancellationToken: cancellationToken
		);

		// Act - Try to register the same definition
		var registerVersion = async () => await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var registerVersionException = await registerVersion.Should().ThrowAsync<RpcException>();
		registerVersionException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		registerVersionException.Which.Message.Should().Contain("Schema definition has not changed");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task preserves_original_data_format_in_registered_version(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = Faker.Lorem.Text();
		var newDefinition = Faker.Lorem.Text();

		// Create initial schema with Protobuf format
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(originalDefinition),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Protobuf,
					Compatibility = CompatibilityMode.Backward
				}
			},
			cancellationToken: cancellationToken
		);

		// Act
		var registerSchemaVersionResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
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
		registerSchemaVersionResult.Should().NotBeNull();

		listRegisteredSchemasResult.Schemas.Should().ContainSingle();
		listRegisteredSchemasResult.Schemas.First().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.First().DataFormat.Should().Be(SchemaFormat.Protobuf);
		listRegisteredSchemasResult.Schemas.First().VersionNumber.Should().Be(2);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task generates_unique_schema_version_ids_for_different_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var secondDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var thirdDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema
		var firstResult = await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = originalDefinition,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.Backward
				}
			},
			cancellationToken: cancellationToken
		);

		// Act
		var secondResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = secondDefinition
			},
			cancellationToken: cancellationToken
		);

		var thirdResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = thirdDefinition
			},
			cancellationToken: cancellationToken
		);

		var listSchemaVersionsResult = await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = schemaName,
				IncludeDefinition = true
			},
			cancellationToken: cancellationToken
		);

		// Assert
		firstResult.SchemaVersionId.Should().NotBe(secondResult.SchemaVersionId);
		secondResult.SchemaVersionId.Should().NotBe(thirdResult.SchemaVersionId);
		thirdResult.SchemaVersionId.Should().NotBe(firstResult.SchemaVersionId);

		listSchemaVersionsResult.Versions.Should().HaveCount(3);
		listSchemaVersionsResult.Versions[0].VersionNumber.Should().Be(1);
		listSchemaVersionsResult.Versions[0].SchemaVersionId.Should().Be(firstResult.SchemaVersionId);
		listSchemaVersionsResult.Versions[1].VersionNumber.Should().Be(2);
		listSchemaVersionsResult.Versions[1].SchemaVersionId.Should().Be(secondResult.SchemaVersionId);
		listSchemaVersionsResult.Versions[2].VersionNumber.Should().Be(3);
		listSchemaVersionsResult.Versions[2].SchemaVersionId.Should().Be(thirdResult.SchemaVersionId);
		listSchemaVersionsResult.Versions[2].SchemaDefinition.Should().BeEquivalentTo(thirdDefinition);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task registers_version_with_empty_schema_definition(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = Faker.Lorem.Text();

		// Create initial schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(originalDefinition),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken: cancellationToken
		);

		// Act
		var result = async () => await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.Empty
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var exception = await result.Should().ThrowAsync<RpcException>();
		exception.Which.Status.StatusCode.Should().Be(StatusCode.InvalidArgument);
		exception.Which.Status.Detail.Should().Contain("Schema definition must not be empty");
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task registers_version_with_large_schema_definition(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = Faker.Lorem.Text();
		var largeDefinition = string.Join("", Enumerable.Repeat(Faker.Lorem.Text(), 100));

		// Create initial schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(originalDefinition),
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken: cancellationToken
		);

		// Act
		var result = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(largeDefinition)
			},
			cancellationToken: cancellationToken
		);

		// Assert
		result.VersionNumber.Should().Be(2);
	}

	[Test, Timeout(TestTimeoutMs)]
	[Arguments(SchemaFormat.Json)]
	[Arguments(SchemaFormat.Protobuf)]
	[Arguments(SchemaFormat.Avro)]
	[Arguments(SchemaFormat.Bytes)]
	public async Task registers_version_for_different_data_formats(
		SchemaFormat dataFormat, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var newDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = originalDefinition,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = dataFormat,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken: cancellationToken
		);

		// Act
		var registerSchemaVersionResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = newDefinition
			},
			cancellationToken: cancellationToken
		);

		var listSchemaVersionsResult = await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = schemaName,
				IncludeDefinition = true
			},
			cancellationToken: cancellationToken
		);

		// Assert
		registerSchemaVersionResult.VersionNumber.Should().Be(2);
		listSchemaVersionsResult.Versions.Should().HaveCount(2);
		listSchemaVersionsResult.Versions.Last().SchemaDefinition.Should().BeEquivalentTo(newDefinition);
	}

	[Test, Timeout(TestTimeoutMs)]
	[Arguments(CompatibilityMode.Backward)]
	[Arguments(CompatibilityMode.Forward)]
	[Arguments(CompatibilityMode.Full)]
	[Arguments(CompatibilityMode.None)]
	public async Task registers_version_for_different_compatibility_modes(
		CompatibilityMode compatibilityMode, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var originalDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());
		var newDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text());

		// Create initial schema
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = originalDefinition,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					DataFormat = SchemaFormat.Json,
					Compatibility = compatibilityMode
				}
			},
			cancellationToken: cancellationToken
		);

		// Act
		var registerSchemaVersionResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = newDefinition
			},
			cancellationToken: cancellationToken
		);

		var listSchemaVersionsResult = await Client.ListSchemaVersionsAsync(
			new ListSchemaVersionsRequest {
				SchemaName = schemaName,
				IncludeDefinition = true
			},
			cancellationToken: cancellationToken
		);

		// Assert
		registerSchemaVersionResult.VersionNumber.Should().Be(2);
		listSchemaVersionsResult.Versions.Should().HaveCount(2);
		listSchemaVersionsResult.Versions.Last().SchemaDefinition.Should().BeEquivalentTo(newDefinition);
	}
}
