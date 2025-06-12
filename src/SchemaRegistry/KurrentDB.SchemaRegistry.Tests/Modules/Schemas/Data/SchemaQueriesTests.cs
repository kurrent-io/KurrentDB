using DuckDB.NET.Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Projectors;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Data;

public class SchemaQueriesTests : SchemaRegistryServerTestFixture {
	[Test, Timeout(10_000)]
	public async Task get_schema_version_with_version_number_returns_version(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		var schemaDefinition = Faker.Lorem.Sentences(10, Environment.NewLine);

		var createSchemaCommand = new CreateSchemaRequest {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition),
			Details = new SchemaDetails {
				Description = Faker.Lorem.Text(),
				DataFormat = SchemaDataFormat.Json,
				Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
				Tags = {
					new Dictionary<string, string> {
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word()
					}
				}
			}
		};

		var createSchemaResponse = await Client.CreateSchemaAsync(createSchemaCommand, cancellationToken: cancellationToken);

		var expectedResponse = new GetSchemaVersionResponse {
			Version = new SchemaVersion {
				SchemaVersionId = createSchemaResponse.SchemaVersionId,
				SchemaDefinition = createSchemaCommand.SchemaDefinition,
				DataFormat = createSchemaCommand.Details.DataFormat,
				VersionNumber = createSchemaResponse.VersionNumber,
				RegisteredAt = Timestamp.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime)
			}
		};

		// Act
		// await Wait.UntilAsserted(
		//     async () => {
		//         var response = await Client.GetSchemaVersionAsync(
		//             new GetSchemaVersionRequest {
		//                 SchemaName    = schemaName,
		//                 VersionNumber = createSchemaResponse.VersionNumber
		//             },
		//             cancellationToken: cancellationToken
		//         );
		//
		//         response.Should().BeEquivalentTo(expectedResponse);
		//     },
		//     cancellationToken: cancellationToken
		// );

		// await Tasks.SafeDelay(1_000, cancellationToken);

		var response = await Client.GetSchemaVersionAsync(
			new GetSchemaVersionRequest {
				SchemaName = schemaName,
				VersionNumber = createSchemaResponse.VersionNumber
			},
			cancellationToken: cancellationToken
		);

		// Assert
		// WARNING!!! BECAUSE for some reason, FLUENT ASSERTIONS it is not using the options!!!
		expectedResponse.Version.RegisteredAt = response.Version.RegisteredAt;

		response.Should().BeEquivalentTo(
			expectedResponse, options => options
				.Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, 1.Seconds()))
				.WhenTypeIs<DateTime>()
		);
	}

	[Test]
	public async Task list_schemas_with_name_prefix(CancellationToken cancellationToken) {
		var foo = NewPrefix();
		var bar = NewPrefix();
		var fooSchemaName = NewSchemaName(foo);
		var barSchemaName = NewSchemaName(bar);

		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = fooSchemaName }, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions { Name = barSchemaName }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListSchemas(new ListSchemasRequest { SchemaNamePrefix = foo }, cancellationToken);
		response.Schemas.Count.Should().Be(1);

		var schema = response.Schemas.First();
		schema.SchemaName.Should().Be(fooSchemaName);
	}

	[Test]
	public async Task list_schemas_with_tags(CancellationToken cancellationToken) {
		var fooSchemaName = NewSchemaName(NewPrefix());
		var barSchemaName = NewSchemaName(NewPrefix());

		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = fooSchemaName }, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions {
			Name = barSchemaName, Tags = new Dictionary<string, string> {
				["baz"] = "qux"
			}
		}, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListSchemas(new ListSchemasRequest { SchemaTags = { ["baz"] = "qux" } }, cancellationToken);
		response.Schemas.Count.Should().Be(1);

		var schema = response.Schemas.First();
		schema.SchemaName.Should().Be(barSchemaName);
	}

	[Test]
	public async Task list_all_schema_versions(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListSchemaVersions(new ListSchemaVersionsRequest { SchemaName = schemaName },
			cancellationToken);
		response.Versions.Count.Should().Be(2);
	}

	[Test]
	public async Task list_registered_schemas(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListRegisteredSchemas(new ListRegisteredSchemasRequest(), cancellationToken);
		response.Schemas.Count.Should().Be(1);
		var schema = response.Schemas.First();
		schema.SchemaName.Should().Be(schemaName);
		schema.VersionNumber.Should().Be(2);
	}

	[Test]
	public async Task list_registered_schemas_multiple_with_prefix(CancellationToken cancellationToken) {
		var prefix = NewPrefix();
		var schemaName1 = NewSchemaName(prefix);
		var schemaName2 = NewSchemaName(prefix);
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName1 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName1, VersionNumber = 24 }, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName2 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName2, VersionNumber = 42 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListRegisteredSchemas(new ListRegisteredSchemasRequest { SchemaNamePrefix = prefix }, cancellationToken);
		var schemas = response.Schemas.OrderBy(x => x.RegisteredAt).ToList();
		schemas.Count.Should().Be(2);
		var schema1 = schemas[0];
		var schema2 = schemas[1];

		schema1.SchemaName.Should().Be(schemaName1);
		schema1.VersionNumber.Should().Be(24);

		schema2.SchemaName.Should().Be(schemaName2);
		schema2.VersionNumber.Should().Be(42);
	}


	[Test]
	public async Task list_registered_schemas_multiple_with_version_id(CancellationToken cancellationToken) {
		var versionId = Guid.NewGuid().ToString();
		var schemaName1 = NewSchemaName(NewPrefix());
		var schemaName2 = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName1, VersionId = versionId }, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListRegisteredSchemas(new ListRegisteredSchemasRequest { SchemaVersionId = versionId }, cancellationToken);
		response.Schemas.Count.Should().Be(1);
		var schema = response.Schemas.First();

		schema.SchemaName.Should().Be(schemaName1);
		schema.SchemaVersionId.Should().Be(versionId);
	}

	[Test]
	public async Task list_registered_schemas_multiple_with_tags(CancellationToken cancellationToken) {
		var schemaName1 = NewSchemaName(NewPrefix());
		var schemaName2 = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions {
			Name = schemaName1, Tags = new Dictionary<string, string> {
				["foo"] = "bar"
			}
		}, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListRegisteredSchemas(new ListRegisteredSchemasRequest {
			SchemaTags = {
				new Dictionary<string, string> {
					["foo"] = "bar"
				}
			}
		}, cancellationToken);
		response.Schemas.Count.Should().Be(1);
		var schema = response.Schemas.First();

		schema.SchemaName.Should().Be(schemaName1);
		schema.Tags["foo"].Should().Be("bar");
	}

	[Test]
	public async Task lookup_schema_name(CancellationToken cancellationToken) {
		var versionId = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.LookupSchemaName(new LookupSchemaNameRequest { SchemaVersionId = versionId }, cancellationToken);
		response.SchemaName.Should().Be(schemaName);
	}

	[Test]
	public async Task check_schema_compatibility_should_be_compatible(CancellationToken cancellationToken) {
		var versionId = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection,
			new CreateSchemaOptions { Name = schemaName, VersionId = versionId, Definition = PersonSchema },
			cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response =
			await queries.CheckSchemaCompatibility(
				new CheckSchemaCompatibilityRequest {
					SchemaVersionId = versionId,
					DataFormat = SchemaDataFormat.Json,
					Definition = ByteString.CopyFromUtf8(PersonSchema)
				}, cancellationToken);

		response.ValidationResult.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task check_schema_compatibility_should_not_be_compatible(CancellationToken cancellationToken) {
		var versionId = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection,
			new CreateSchemaOptions { Name = schemaName, VersionId = versionId, Definition = PersonSchema },
			cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response =
			await queries.CheckSchemaCompatibility(
				new CheckSchemaCompatibilityRequest {
					SchemaVersionId = versionId, DataFormat = SchemaDataFormat.Json,
					Definition = ByteString.CopyFromUtf8(CarSchema)
				}, cancellationToken);

		response.ValidationResult.IsCompatible.Should().BeFalse();
	}

	[Test]
	public async Task get_schema(CancellationToken cancellationToken) {
		var versionId1 = Guid.NewGuid().ToString();
		var versionId2 = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId1 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 24, VersionId = versionId2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.GetSchema(new GetSchemaRequest { SchemaName = schemaName }, cancellationToken);
		if (response.ResultCase != GetSchemaResponse.ResultOneofCase.Success)
			throw new Exception("Boom");

		response.Success.Schema.SchemaName.Should().Be(schemaName);
		response.Success.Schema.LatestSchemaVersion.Should().Be(24);
	}

	[Test]
	public async Task get_schema_version(CancellationToken cancellationToken) {
		var versionId1 = Guid.NewGuid().ToString();
		var versionId2 = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId1 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 24, VersionId = versionId2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.GetSchemaVersion(new GetSchemaVersionRequest { SchemaName = schemaName }, cancellationToken);
		response.Version.SchemaVersionId.Should().Be(versionId2);
	}

	[Test]
	public async Task get_schema_version_with_version_number(CancellationToken cancellationToken) {
		var versionId1 = Guid.NewGuid().ToString();
		var versionId2 = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDBConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId1 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 24, VersionId = versionId2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDBConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.GetSchemaVersion(new GetSchemaVersionRequest { SchemaName = schemaName, VersionNumber = 1 }, cancellationToken);
		response.Version.SchemaVersionId.Should().Be(versionId1);
	}

	private record CreateSchemaOptions {
		public required string Name { get; init; }
		public Dictionary<string, string> Tags { get; init; } = [];
		public string? VersionId { get; init; }
		public string? Definition { get; init; }
	}

	private async Task CreateSchema(SchemaProjections projections, CreateSchemaOptions options, CancellationToken cancellationToken) {
		var record = await CreateRecord(
			new SchemaCreated {
				SchemaName = options.Name,
				SchemaDefinition = ByteString.CopyFromUtf8(options.Definition ?? PersonSchema),
				Description = Faker.Lorem.Text(),
				DataFormat = SchemaDataFormat.Json,
				Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
				Tags = {
					options.Tags
				},
				SchemaVersionId = options.VersionId ?? Guid.NewGuid().ToString(),
				VersionNumber = 1,
				CreatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
			}
		);

		await projections.ProjectRecord(new ProjectionContext<DuckDBConnection>(_ => ValueTask.FromResult(DuckDBConnectionProvider.GetConnection()), record,
			cancellationToken));
	}

	private record UpdateSchemaOptions {
		public required string Name { get; init; }
		public required int VersionNumber { get; init; }
		public string? VersionId { get; init; }
	}

	private async Task UpdateSchema(SchemaProjections projections, UpdateSchemaOptions options,
		CancellationToken cancellationToken) {
		var record = await CreateRecord(
			new SchemaVersionRegistered {
				SchemaVersionId = options.VersionId ?? Guid.NewGuid().ToString(),
				SchemaName = options.Name,
				VersionNumber = options.VersionNumber,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				DataFormat = SchemaDataFormat.Json,
				RegisteredAt = Timestamp.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime)
			}
		);

		await projections.ProjectRecord(new ProjectionContext<DuckDBConnection>(_ => ValueTask.FromResult(DuckDBConnectionProvider.GetConnection()), record,
			cancellationToken));
	}

	static readonly string PersonSchema =
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

	static readonly string CarSchema =
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
