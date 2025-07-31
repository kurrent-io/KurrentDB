// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Grpc.Core;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class DeleteSchemaIntegrationTests : SchemaApplicationTestFixture {
	[Test]
	public async Task deletes_schema_successfully(CancellationToken cancellationToken) {
		// Arrange
		var prefix = NewPrefix();
		var schemaName = NewSchemaName(prefix);

		await CreateSchema(schemaName, cancellationToken);

		// Act
		var deleteSchemaResult = await DeleteSchema(schemaName, cancellationToken);

		// Assert
		var listSchemasResult = await ListRegisteredSchemas(prefix, cancellationToken);

		deleteSchemaResult.Should().NotBeNull();
		listSchemasResult.Should().NotBeNull();
		listSchemasResult.Schemas.Should().BeEmpty();
	}

	[Test]
	public async Task deletes_schema_with_multiple_versions_successfully(CancellationToken cancellationToken) {
		// Arrange
		var prefix = NewPrefix();
		var schemaName = NewSchemaName(prefix);

		var v1 = NewJsonSchema();
		var v2 = v1.AddOptional("email", JsonObjectType.String);
		var v3 = v2.AddOptional("age", JsonObjectType.Integer);

		await CreateSchema(schemaName, v1, cancellationToken);
		await RegisterSchemaVersion(schemaName, v2, cancellationToken);
		await RegisterSchemaVersion(schemaName, v3, cancellationToken);

		// Act
		var deleteSchemaResult = await DeleteSchema(schemaName, cancellationToken);

		// Assert
		var listSchemasResult = await ListRegisteredSchemas(prefix, cancellationToken);

		deleteSchemaResult.Should().NotBeNull();
		listSchemasResult.Should().NotBeNull();
		listSchemasResult.Schemas.Should().BeEmpty();
	}

	[Test]
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
