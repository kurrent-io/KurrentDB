// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Indexes.User;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.Migration;

partial class MigrationTests {
	// A migrated database should have the same schema a fresh install creates from 1_Schema.sql.

	// Must match the user index table SetupV0Schema creates
	private const string UserIndexName = "myindex";
	private const string UserIndexFieldName = "myfield";

	private static void StartFromV0(DuckDBAdvancedConnection connection) {
		// nothing to do, the constructor leaves the database at V0
	}

	// A database upgraded by the originally shipped V1 (V1A), which added record_id with
	// DEFAULT ''::BLOB.
	private static void StartFromV1A(DuckDBAdvancedConnection connection) {
		IndexingDbSchema.UpgradeToV1A(connection);
	}

	[Fact]
	public async Task SchemaAfterMigratingFromV0MatchesFreshInstall() =>
		await AssertSchemaMatchesFreshInstall(StartFromV0, startingVersion: 0);

	// a database that was upgraded to V1 before the V1 migration was patched to V1B
	[Fact]
	public async Task SchemaAfterMigratingFromV1AMatchesFreshInstall() =>
		await AssertSchemaMatchesFreshInstall(StartFromV1A, startingVersion: 1);

	private async Task AssertSchemaMatchesFreshInstall(
		Action<DuckDBAdvancedConnection> migrateFromV0ToStartingVersion,
		int startingVersion) {

		// Apply initial migrations, allows the test to choose between A/B variations of migrations.
		migrateFromV0ToStartingVersion(_connection);

		// Apply the rest of the migrations in turn
		IndexingDbSchema.PerformMigration(
			startingVersion,
			IndexingDbSchema.TargetVersion,
			_connection,
			IndexingDbSchema.MigrationActions,
			NullLogger<IndexingDbSchema>.Instance);

		var expected = await ReadFreshInstallShape();
		var actual = ReadShape(_connection);

		Assert.Equal(expected, actual);
	}

	private async Task<List<ColumnShape>> ReadFreshInstallShape() {
		var dbPath = Fixture.GetFilePathFor($"{GetType().Name}.fresh.db");
		await using DuckDBAdvancedConnection connection = new() { ConnectionString = $"Data Source={dbPath};" };
		connection.Open();

		// An empty database takes the fresh setup branch, which builds the schema from 1_Schema.sql
		IndexingDbSchema.PerformMigration(connection);

		// 1_Schema.sql contains no user index tables - they are created on demand - so create one the
		// way production does, giving the comparison something to hold the migrated one against. The
		// index name and field type must match the table SetupV0Schema creates.
		new UserIndexSql<Int32Field>(UserIndexName, UserIndexFieldName).CreateUserIndex(connection);

		return ReadShape(connection);
	}

	private static List<ColumnShape> ReadShape(DuckDBAdvancedConnection connection) =>
		connection.ExecuteQuery<ColumnShape, SchemaShapeQuery>().ToList();

	private readonly record struct ColumnShape(
		string Table,
		long Position,
		string Name,
		string DataType,
		string IsNullable,
		string Default);

	// Everything in the database, including user index tables, so a table or view added to 1_Schema.sql
	// or created by a later migration gets compared without anyone having to remember to add it here.
	private readonly struct SchemaShapeQuery : IQuery<ColumnShape> {
		public static ReadOnlySpan<byte> CommandText => """
			select
				table_name,
				cast(ordinal_position as bigint),
				column_name,
				data_type,
				is_nullable,
				coalesce(column_default, 'NONE')
			from information_schema.columns
			order by table_name, ordinal_position;
			"""u8;

		public static ColumnShape Parse(ref DataChunk.Row row) => new(
			Table: row.ReadString(),
			Position: row.ReadInt64(),
			Name: row.ReadString(),
			DataType: row.ReadString(),
			IsNullable: row.ReadString(),
			Default: row.ReadString());
	}
}
