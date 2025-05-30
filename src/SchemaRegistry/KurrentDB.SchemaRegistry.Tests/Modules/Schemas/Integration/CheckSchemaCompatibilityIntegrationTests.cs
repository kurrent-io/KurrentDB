// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Tests.Fixtures;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class CheckSchemaCompatibilityIntegrationTests : SchemaApplicationTestFixture {
	private const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_matches(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();

		// Arrange
		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(PersonSchema),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Forward,
				Description = Faker.Lorem.Text(),
			},
		}, cancellationToken: cancellationToken);

		var checkResponse = await Client.CheckSchemaCompatibilityAsync(new CheckSchemaCompatibilityRequest {
			SchemaName = schemaName,
			DataFormat = SchemaDataFormat.Json,
			Definition = ByteString.CopyFromUtf8(PersonSchema)
		}, cancellationToken: cancellationToken);

		checkResponse.ValidationResult.IsCompatible.Should().BeTrue();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_incompatible(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();

		// Arrange
		await Client.CreateSchemaAsync(new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(PersonSchema),
			Details = new SchemaDetails {
				DataFormat = SchemaDataFormat.Json,
				Compatibility = CompatibilityMode.Forward,
				Description = Faker.Lorem.Text(),
			},
		}, cancellationToken: cancellationToken);

		var checkResponse = await Client.CheckSchemaCompatibilityAsync(new CheckSchemaCompatibilityRequest {
			SchemaName = schemaName,
			DataFormat = SchemaDataFormat.Json,
			Definition = ByteString.CopyFromUtf8(CarSchema)
		}, cancellationToken: cancellationToken);

		checkResponse.ValidationResult.IsCompatible.Should().BeFalse();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_schema_name_not_found(CancellationToken cancellationToken) {
		var ex = await FluentActions.Awaiting(async () => await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = Guid.NewGuid().ToString(),
				DataFormat = SchemaDataFormat.Json,
				Definition = ByteString.CopyFromUtf8(CarSchema)
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
				Definition = ByteString.CopyFromUtf8(CarSchema)
			},
			cancellationToken: cancellationToken
		)).Should().ThrowAsync<RpcException>();

		ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_backward_all_is_compatible(CancellationToken cancellationToken) {
		var schemaName = Guid.NewGuid().ToString();
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.BackwardAll,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = ByteString.CopyFromUtf8(
					"""
					{
						"type": "object",
						"properties": {
							"field1": { "type": "string" },
							"field2": { "type": "integer" }
						},
						"required": ["field1"]
					}
					"""
				)
			},
			cancellationToken: cancellationToken
		);

		await Client.RegisterSchemaVersionAsync(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(
					"""
					{
						"type": "object",
						"properties": {
							"field1": { "type": "string" }
						}
					}
					"""
				)
			},
			cancellationToken: cancellationToken
		);

		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = ByteString.CopyFromUtf8(
					"""
					{
					   "type": "object",
					   "properties": {
					       "field1": { "type": "string" },
					       "field3": { "type": "boolean" }
					   }
					}
					"""
				)
			},
			cancellationToken: cancellationToken
		);

		response.ValidationResult.IsCompatible.Should().BeTrue();
		response.ValidationResult.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task check_schema_compatibility_backward_all_is_incompatible(CancellationToken cancellationToken) {
		var schemaName = Guid.NewGuid().ToString();
		await Client.CreateSchemaAsync(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				Details = new SchemaDetails {
					DataFormat = SchemaDataFormat.Json,
					Compatibility = CompatibilityMode.BackwardAll,
					Description = Faker.Lorem.Text(),
				},
				SchemaDefinition = ByteString.CopyFromUtf8(
					"""
					{
					  "type": "object",
					  "properties": {
					      "field1": { "type": "string" },
					      "field2": { "type": "string" }
					  }
					}
					"""
				)
			},
			cancellationToken: cancellationToken
		);

		var response = await Client.CheckSchemaCompatibilityAsync(
			new CheckSchemaCompatibilityRequest {
				SchemaName = schemaName,
				DataFormat = SchemaDataFormat.Json,
				Definition = ByteString.CopyFromUtf8(
					"""
					{
					  "type": "object",
					  "properties": {
					      "field1": { "type": "integer" },
					      "field2": { "type": "string" },
					      "field3": { "type": "boolean" }
					  },
					  "required": ["field2", "field3"]
					}
					"""
				)
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

	const string PersonSchema =
		// lang=JSON
		"""
		{
		  "$schema": "http://json-schema.org/draft-07/schema#",

		  "title": "Person",
		  "type": "object",
		  "properties": {
		    "firstName": {
		      "type": "string"
		    },
		    "lastName": {
		      "type": "string"
		    },
		    "age": {
		      "type": "integer",
		      "minimum": 0
		    },
		    "email": {
		      "type": "string",
		      "format": "email"
		    }
		  },
		  "required": ["firstName", "lastName"]
		}
		""";

	const string CarSchema =
		// lang=JSON
		"""
		{
		  "$schema": "http://json-schema.org/draft-07/schema#",
		  "title": "Car",
		  "type": "object",
		  "properties": {
		    "make": {
		      "type": "string"
		    },
		    "model": {
		      "type": "string"
		    },
		    "year": {
		      "type": "integer",
		      "minimum": 1886
		    },
		    "vin": {
		      "type": "string"
		    },
		    "color": {
		      "type": "string"
		    }
		  },
		  "required": ["make", "model", "year", "vin"]
		}
		""";
}
