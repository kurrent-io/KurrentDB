// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class RegisterSchemaVersionIntegrationTests : SchemaApplicationTestFixture {
	[Test]
	public async Task registers_new_schema_version_successfully(CancellationToken cancellationToken) {
		// Arrange
		var prefix = NewSchemaName();
		var schemaName = NewSchemaName(prefix);
		var v1 = NewJsonSchema();
		var v2 = v1.AddOptional("email", JsonObjectType.String);

		await CreateSchema(schemaName, v1, cancellationToken);

		// Act
		var registerSchemaVersionResult = await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = v2.ToByteString()
			},
			cancellationToken: cancellationToken
		);

		// Assert
		registerSchemaVersionResult.Should().NotBeNull();
		registerSchemaVersionResult.VersionNumber.Should().Be(2);
	}

	[Test]
	public async Task throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var v1 = NewJsonSchema();

		// Act
		var act = async () => await Client.RegisterSchemaVersionAsync(new RegisterSchemaVersionRequest {
			SchemaName = nonExistentSchemaName,
			SchemaDefinition = v1.ToByteString(),
		}, cancellationToken: cancellationToken);

		// Assert
		var exception = await act.Should().ThrowAsync<RpcException>();
		exception.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test]
	public async Task throws_exception_when_schema_definition_has_not_changed(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var v1 = NewJsonSchema();

		await CreateSchema(schemaName, v1, cancellationToken);

		// Act
		var act = async () => await Client.RegisterSchemaVersionAsync(new RegisterSchemaVersionRequest {
			SchemaName = schemaName,
			SchemaDefinition = v1.ToByteString(),
		}, cancellationToken: cancellationToken);

		// Assert
		var exception = await act.Should().ThrowAsync<RpcException>();
		exception.Which.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
		exception.Which.Message.Should().Contain("Schema definition has not changed");
	}
}
