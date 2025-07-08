using DuckDB.NET.Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Quack;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Projectors;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Data;

[NotInParallel]
[Skip("Flaky")]
public class ProjectionsTests : SchemaApplicationTestFixture {
	[Test]
	public async Task setup_creates_tables_and_indexes(CancellationToken cancellationToken) {
		var connection = DuckDbConnectionProvider.GetConnection();

		var projection = new SchemaProjections();

		var setup = () => projection.Setup(connection, cancellationToken);

		await setup.ShouldNotThrowAsync<DuckDBException>();

		var schemasTableExists = await connection.TableExists("schemas");
		schemasTableExists.Should().BeTrue();

		var versionsTableExists = await connection.TableExists("schema_versions");
		versionsTableExists.Should().BeTrue();
	}

	[Test]
	public async Task on_schema_created(CancellationToken cancellationToken) {
		var connection = DuckDbConnectionProvider.GetConnection();

		var projection = new SchemaProjections();

		await projection.Setup(connection, cancellationToken);

		var schemaName = NewSchemaName();

		var record = await CreateRecord(
			new SchemaCreated {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Description = Faker.Lorem.Text(),
				DataFormat = SchemaDataFormat.Json,
				Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
				Tags = {
					new Dictionary<string, string> {
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word()
					}
				},
				SchemaVersionId = Guid.NewGuid().ToString(),
				VersionNumber = 1,
				CreatedAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
			}
		);

		await projection.ProjectRecord(new ProjectionContext<DuckDBAdvancedConnection>(_ => ValueTask.FromResult(DuckDbConnectionProvider.GetConnection()), record,
			cancellationToken));

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var getSchemaResponse = await queries.GetSchema(new GetSchemaRequest { SchemaName = schemaName }, cancellationToken);

		var getSchemaVersionResponse = await queries.GetSchemaVersion(
			new() {
				SchemaName = getSchemaResponse.Schema.SchemaName,
				VersionNumber = getSchemaResponse.Schema.LatestSchemaVersion
			},
			cancellationToken
		);
	}

	[Test]
	public async Task on_schema_version_registered(CancellationToken cancellationToken) {
		var connection = DuckDbConnectionProvider.GetConnection();

		var projection = new SchemaProjections();

		await projection.Setup(connection, cancellationToken);

		var schemaName = NewSchemaName();

		var schemaCreatedRecord = await CreateRecord(
			new SchemaCreated {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Description = Faker.Lorem.Text(),
				DataFormat = SchemaDataFormat.Json,
				Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
				Tags = {
					new Dictionary<string, string> {
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word()
					}
				},
				SchemaVersionId = Guid.NewGuid().ToString(),
				VersionNumber = 1,
				CreatedAt = Timestamp.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime)
			}
		);

		await projection.ProjectRecord(new ProjectionContext<DuckDBAdvancedConnection>(_ => ValueTask.FromResult(DuckDbConnectionProvider.GetConnection()),
			schemaCreatedRecord, cancellationToken));

		var schemaVersionRegisteredRecord = await CreateRecord(
			new SchemaVersionRegistered {
				SchemaVersionId = Guid.NewGuid().ToString(),
				SchemaName = schemaName,
				VersionNumber = 2,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				DataFormat = SchemaDataFormat.Json,
				RegisteredAt = Timestamp.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime)
			}
		);

		await projection.ProjectRecord(new ProjectionContext<DuckDBAdvancedConnection>(_ => ValueTask.FromResult(DuckDbConnectionProvider.GetConnection()),
			schemaVersionRegisteredRecord, cancellationToken));

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var getSchemaVersionResponse = await queries.GetSchemaVersion(
			new() {
				SchemaName = schemaName,
				VersionNumber = 2
			},
			cancellationToken
		);
	}

	[Test]
	public async Task on_schema_deleted(CancellationToken cancellationToken) {
		var connection = DuckDbConnectionProvider.GetConnection();

		var projection = new SchemaProjections();

		await projection.Setup(connection, cancellationToken);

		var schemaName = NewSchemaName();

		var schemaCreatedRecord = await CreateRecord(
			new SchemaCreated {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
				Description = Faker.Lorem.Text(),
				DataFormat = SchemaDataFormat.Json,
				Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
				Tags = {
					new Dictionary<string, string> {
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word(),
						[Faker.Lorem.Word()] = Faker.Lorem.Word()
					}
				},
				SchemaVersionId = Guid.NewGuid().ToString(),
				VersionNumber = 1,
				CreatedAt = Timestamp.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime)
			}
		);

		await projection.ProjectRecord(new ProjectionContext<DuckDBAdvancedConnection>(_ => ValueTask.FromResult(DuckDbConnectionProvider.GetConnection()),
			schemaCreatedRecord, cancellationToken));

		var schemaDeletedRecord = await CreateRecord(new SchemaDeleted { SchemaName = schemaName });

		await projection.ProjectRecord(new ProjectionContext<DuckDBAdvancedConnection>(_ => ValueTask.FromResult(DuckDbConnectionProvider.GetConnection()),
			schemaDeletedRecord, cancellationToken));

		var queries = new SchemaQueries(DuckDbConnectionProvider, new NJsonSchemaCompatibilityManager());

		var listSchemasResponse = await queries.ListSchemas(new() { SchemaNamePrefix = schemaName }, cancellationToken);
		//var listVersionsResponse = await queries.ListSchemaVersions(new() { SchemaName = schemaName }, cancellationToken);
	}
}
