// ReSharper disable ArrangeTypeMemberModifiers

using DuckDB.NET.Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Projectors;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using NJsonSchema;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Data;

public class SchemaQueriesTests : SchemaApplicationTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_schemas_with_name_prefix(CancellationToken cancellationToken) {
		var foo = NewPrefix();
		var bar = NewPrefix();
		var fooSchemaName = NewSchemaName(foo);
		var barSchemaName = NewSchemaName(bar);

		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = fooSchemaName }, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions { Name = barSchemaName }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListSchemas(new ListSchemasRequest { SchemaNamePrefix = foo }, cancellationToken);
		response.Schemas.Count.Should().Be(1);

		var schema = response.Schemas.First();
		schema.SchemaName.Should().Be(fooSchemaName);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_schemas_with_tags(CancellationToken cancellationToken) {
		var fooSchemaName = NewSchemaName(NewPrefix());
		var barSchemaName = NewSchemaName(NewPrefix());

		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = fooSchemaName }, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions {
			Name = barSchemaName, Tags = new Dictionary<string, string> {
				["baz"] = "qux"
			}
		}, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListSchemas(new ListSchemasRequest { SchemaTags = { ["baz"] = "qux" } }, cancellationToken);
		response.Schemas.Count.Should().Be(1);

		var schema = response.Schemas.First();
		schema.SchemaName.Should().Be(barSchemaName);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_all_schema_versions(CancellationToken cancellationToken) {
		var schemaName = NewSchemaName();
		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListSchemaVersions(new ListSchemaVersionsRequest { SchemaName = schemaName },
			cancellationToken);
		response.Versions.Count.Should().Be(2);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schemas_multiple_with_version_id(CancellationToken cancellationToken) {
		var versionId = Guid.NewGuid().ToString();
		var schemaName1 = NewSchemaName(NewPrefix());
		var schemaName2 = NewSchemaName(NewPrefix());
		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName1, VersionId = versionId }, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.ListRegisteredSchemas(new ListRegisteredSchemasRequest { SchemaVersionId = versionId }, cancellationToken);
		response.Schemas.Count.Should().Be(1);
		var schema = response.Schemas.First();

		schema.SchemaName.Should().Be(schemaName1);
		schema.SchemaVersionId.Should().Be(versionId);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task list_registered_schemas_multiple_with_tags(CancellationToken cancellationToken) {
		var schemaName1 = NewSchemaName(NewPrefix());
		var schemaName2 = NewSchemaName(NewPrefix());
		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions {
			Name = schemaName1, Tags = new Dictionary<string, string> {
				["foo"] = "bar"
			}
		}, cancellationToken);
		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

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

	[Test, Timeout(TestTimeoutMs)]
	public async Task lookup_schema_name(CancellationToken cancellationToken) {
		var versionId = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.LookupSchemaName(new LookupSchemaNameRequest { SchemaVersionId = versionId }, cancellationToken);
		response.SchemaName.Should().Be(schemaName);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task check_schema_compatibility_should_be_compatible(CancellationToken cancellationToken) {
		var versionId = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var schema = NewJsonSchemaDefinition();

		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection,
			new CreateSchemaOptions { Name = schemaName, VersionId = versionId, Definition = schema.ToJson() },
			cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response =
			await queries.CheckSchemaCompatibility(
				new CheckSchemaCompatibilityRequest {
					SchemaVersionId = versionId,
					DataFormat = SchemaDataFormat.Json,
					Definition = schema.ToByteString()
				}, cancellationToken);

		response.Success.Should().NotBeNull();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema(CancellationToken cancellationToken) {
		var versionId1 = Guid.NewGuid().ToString();
		var versionId2 = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId1 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 24, VersionId = versionId2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.GetSchema(new GetSchemaRequest { SchemaName = schemaName }, cancellationToken);

		response.Schema.SchemaName.Should().Be(schemaName);
		response.Schema.LatestSchemaVersion.Should().Be(24);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_version(CancellationToken cancellationToken) {
		var versionId1 = Guid.NewGuid().ToString();
		var versionId2 = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId1 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 24, VersionId = versionId2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var response = await queries.GetSchemaVersion(new GetSchemaVersionRequest { SchemaName = schemaName }, cancellationToken);
		response.Version.SchemaVersionId.Should().Be(versionId2);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_version_with_version_number(CancellationToken cancellationToken) {
		var versionId1 = Guid.NewGuid().ToString();
		var versionId2 = Guid.NewGuid().ToString();
		var schemaName = NewSchemaName(NewPrefix());
		var connection = DuckDbConnectionProvider.GetConnection();
		var projection = new SchemaProjections();
		await projection.Setup(connection, cancellationToken);

		await CreateSchema(projection, new CreateSchemaOptions { Name = schemaName, VersionId = versionId1 }, cancellationToken);
		await UpdateSchema(projection, new UpdateSchemaOptions { Name = schemaName, VersionNumber = 24, VersionId = versionId2 }, cancellationToken);

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

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
				SchemaDefinition = ByteString.CopyFromUtf8(options.Definition ?? NewJsonSchemaDefinition().ToJson()),
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

		await projections.ProjectRecord(new ProjectionContext<DuckDBConnection>(_ => ValueTask.FromResult(DuckDbConnectionProvider.GetConnection()), record,
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

		await projections.ProjectRecord(new ProjectionContext<DuckDBConnection>(_ => ValueTask.FromResult(DuckDbConnectionProvider.GetConnection()), record,
			cancellationToken));
	}
}
