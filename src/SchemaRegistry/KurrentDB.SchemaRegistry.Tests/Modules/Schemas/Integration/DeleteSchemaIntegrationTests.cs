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
		var prefix = NewPrefix();
		var schemaName = NewSchemaName(prefix);
		var v1 = NewJsonSchemaDefinition();

		await CreateSchema(schemaName, v1, cancellationToken);

		// Act
		var deleteSchemaResult = await DeleteSchema(schemaName, cancellationToken);

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

	// [Test, Timeout(TestTimeoutMs)]
	// public async Task deletes_schema_with_multiple_versions_successfully(CancellationToken cancellationToken) {
	// 	// Arrange
	// 	var schemaName = NewSchemaName();
	// 	await CreateSchemaAsync(schemaName: schemaName, cancellationToken: cancellationToken);
	//
	// 	// Register additional versions
	// 	await Client.RegisterSchemaVersionAsync(
	// 		new RegisterSchemaVersionRequest {
	// 			SchemaName = schemaName,
	// 			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
	// 		},
	// 		cancellationToken: cancellationToken
	// 	);
	//
	// 	await Client.RegisterSchemaVersionAsync(
	// 		new RegisterSchemaVersionRequest {
	// 			SchemaName = schemaName,
	// 			SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
	// 		},
	// 		cancellationToken: cancellationToken
	// 	);
	//
	// 	// Act
	// 	var deleteSchemaResult = await Client.DeleteSchemaAsync(
	// 		new DeleteSchemaRequest { SchemaName = schemaName },
	// 		cancellationToken: cancellationToken
	// 	);
	//
	// 	var listSchemasResult = await Client.ListSchemasAsync(
	// 		new ListSchemasRequest {
	// 			SchemaNamePrefix = schemaName
	// 		},
	// 		cancellationToken: cancellationToken
	// 	);
	//
	//
	// 	// Assert
	// 	deleteSchemaResult.Should().NotBeNull();
	// 	listSchemasResult.Should().NotBeNull();
	// 	listSchemasResult.Schemas.Should().BeEmpty();
	// }

	[Test, Timeout(TestTimeoutMs)]
	public async Task throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = NewSchemaName();

		// Act
		var deleteSchema = async () => await DeleteSchema(nonExistentSchemaName, cancellationToken);

		// Assert
		var exception = await deleteSchema.Should().ThrowAsync<RpcException>();
		exception.Which.Status.StatusCode.Should().Be(StatusCode.NotFound);
	}
}
