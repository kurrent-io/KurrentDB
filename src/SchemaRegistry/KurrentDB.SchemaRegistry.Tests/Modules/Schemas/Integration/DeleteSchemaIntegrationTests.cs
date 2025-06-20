// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;


public class DeleteSchemaIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 10_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_schema_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Act
		var deleteSchemaResult = await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken: cancellationToken
		);

		var listSchemasResult = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = schemaName
			},
			cancellationToken: cancellationToken
		);

		// Assert
		deleteSchemaResult.Should().NotBeNull();
		listSchemasResult.Should().NotBeNull();
		listSchemasResult.Schemas.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_schema_with_multiple_versions_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Register additional versions
		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken: cancellationToken
		);

		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken: cancellationToken
		);

		// Act
		var deleteSchemaResult = await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken: cancellationToken
		);

		var listSchemasResult = await Client.ListSchemasAsync(
			new ListSchemasRequest {
				SchemaNamePrefix = schemaName
			},
			cancellationToken: cancellationToken
		);


		// Assert
		deleteSchemaResult.Should().NotBeNull();
		listSchemasResult.Should().NotBeNull();
		listSchemasResult.Schemas.Should().BeEmpty();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

		// Act
		var deleteSchema = async () => await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = nonExistentSchemaName },
			cancellationToken: cancellationToken
		);

		// Assert
		var exception = await deleteSchema.Should().ThrowAsync<RpcException>();
		exception.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_is_already_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		// Delete schema first time
		await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken: cancellationToken
		);

		// Act - Try to delete again
		var deleteSchema = async () => await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken: cancellationToken
		);

		// Assert
		var deleteSchemaException = await deleteSchema.Should().ThrowAsync<RpcException>();
		deleteSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	[Arguments(CompatibilityMode.Backward)]
	[Arguments(CompatibilityMode.Forward)]
	[Arguments(CompatibilityMode.Full)]
	[Arguments(CompatibilityMode.None)]
	public async Task deletes_schema_with_different_compatibility_modes(CompatibilityMode compatibilityMode, CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, compatibility: compatibilityMode, cancellationToken: cancellationToken);

		// Act
		var deleteSchemaResult = await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken: cancellationToken
		);

		// Assert
		deleteSchemaResult.Should().NotBeNull();
	}

	[Test, Timeout(TestTimeoutMs)]
	[Arguments(SchemaFormat.Json)]
	[Arguments(SchemaFormat.Protobuf)]
	[Arguments(SchemaFormat.Avro)]
	[Arguments(SchemaFormat.Bytes)]
	public async Task deletes_schema_with_different_data_formats(SchemaFormat dataFormat, CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, dataFormat: dataFormat, cancellationToken: cancellationToken);

		// Act
		var deleteSchemaResult = await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken: cancellationToken
		);

		// Assert
		deleteSchemaResult.Should().NotBeNull();
	}

	[Test]
	public async Task subsequent_operations_on_deleted_schema_throw_exceptions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);

		await Client.DeleteSchemaAsync(
			new DeleteSchemaRequest { SchemaName = schemaName },
			cancellationToken: cancellationToken
		);

		// Act & Assert - Try various operations on deleted schema
		var updateSchema = async () => await Client.UpdateSchemaAsync(
			new UpdateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					Description = Faker.Lorem.Sentence(),
					Compatibility = CompatibilityMode.Backward,
					DataFormat = SchemaFormat.Json
				},
				UpdateMask = new FieldMask {
					Paths = {
						"Details.Description"
					}
				},
			},
			cancellationToken: cancellationToken
		);
		var updateSchemaException = await updateSchema.Should().ThrowAsync<RpcException>();
		updateSchemaException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);

		var registerVersion = async () => await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
			},
			cancellationToken: cancellationToken
		);
		var registerVersionException = await registerVersion.Should().ThrowAsync<RpcException>();
		registerVersionException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);

		var deleteVersions = async () => await Client.DeleteSchemaVersionsAsync(
			new DeleteSchemaVersionsRequest {
				SchemaName = schemaName,
				Versions = { 1 }
			},
			cancellationToken: cancellationToken
		);
		var deleteVersionsException = await deleteVersions.Should().ThrowAsync<RpcException>();
		deleteVersionsException.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
