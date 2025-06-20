// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class RegisterSchemaVersionIntegrationTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 10_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task registers_new_schema_version_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchemaAsync(schemaName: schemaName, schemaDefinition: v1.ToByteString(), cancellationToken: cancellationToken);

		// Act
		var registerSchemaVersionResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
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
		listRegisteredSchemasResult.Schemas.Last().SchemaName.Should().Be(schemaName);
		listRegisteredSchemasResult.Schemas.Last().VersionNumber.Should().Be(2);
		listRegisteredSchemasResult.Schemas.Last().SchemaDefinition.Should().BeEquivalentTo(v2.ToByteString());
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
	public async Task throws_exception_when_schema_definition_has_not_changed(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchemaDefinition();

		await CreateSchemaAsync(schemaName: schemaName, schemaDefinition: v1.ToByteString(), cancellationToken: cancellationToken);

		// Act
		var registerVersion = async () => await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v1.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		var registerVersionException = await registerVersion.Should().ThrowAsync<RpcException>();
		registerVersionException.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		registerVersionException.Which.Message.Should().Contain("Schema definition has not changed");
	}
}
