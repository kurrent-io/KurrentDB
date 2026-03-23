// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Quack;
using KurrentDB.Core.XUnit.Tests;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Tests.Migration;

public sealed partial class MigrationTests : DirectoryPerTest<MigrationTests> {
	private readonly DuckDBAdvancedConnection _connection;

	public MigrationTests() {
		var dbPath = Fixture.GetFilePathFor($"{GetType().Name}.db");

		_connection = new() { ConnectionString = $"Data Source={dbPath};" };
		_connection.Open();

		// Setup V0 schema
		SetupV0Schema(_connection);
	}

	private void UpgradeTo(int desiredVersion) {
		Assert.Equal(0, IndexingDbSchema.GetVersion(_connection));
		IndexingDbSchema.PerformMigration(_connection, initialSetup: false, desiredVersion);
		Assert.Equal(desiredVersion, IndexingDbSchema.GetVersion(_connection));
	}

	public override async ValueTask DisposeAsync() {
		await _connection.DisposeAsync();
		await base.DisposeAsync();
	}

	private static void SetupV0Schema(DuckDBConnection connection) {
		const string schema = """
		  create table if not exists idx_all (
		    log_position bigint not null,
		    commit_position bigint null,
		    event_number bigint not null,
		    created bigint not null,
		    expires bigint null,
		    stream varchar not null,
		    stream_hash ubigint not null,
		    event_type varchar not null,
		    category varchar not null,
		    is_deleted boolean not null,
		    schema_id varchar null,
		    schema_format varchar not null
		  );

		  create table if not exists idx_user_checkpoints (
		    index_name varchar primary key,
		    log_position bigint not null,
		    commit_position bigint null,
		    created bigint not null
		  );

		  create table if not exists idx_user__MyIndex (
		      log_position bigint not null,
		      commit_position bigint null,
		      event_number bigint not null,
		      created bigint not null,
		      my_field integer not null
		  );

		  create table if not exists idx_metadata(
		    key varchar primary key not null,
		    value varchar
		  );
		  """;

		using var transaction = connection.BeginTransaction();
		var cmd = connection.CreateCommand();
		cmd.Transaction = transaction;

		try {
			cmd.CommandText = schema;
			cmd.ExecuteNonQuery();
		} catch {
			transaction.Rollback();
			throw;
		} finally {
			cmd.Dispose();
		}

		transaction.Commit();
	}

	private readonly struct ColumnNamesQuery : IQuery<ValueTuple<string>, string> {
		public static ReadOnlySpan<byte> CommandText => """
		                                                SELECT column_name
		                                                FROM information_schema.columns
		                                                WHERE table_name = ?;
		                                                """u8;

		public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source)
			=> new(source) { args.Item1 };

		public static string Parse(ref DataChunk.Row row) => row.ReadString();
	}
}
