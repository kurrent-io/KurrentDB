// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine;

partial class ClusterState {
	private static readonly string LatestSchema = $"""
	                                                      CREATE TABLE metadata (
	                                                       version INTEGER NOT NULL,
	                                                       index BIGINT NOT NULL,
	                                                       term BIGINT NOT NULL,
	                                                      );

	                                                      CREATE TABLE database (
	                                                        id VARCHAR PRIMARY KEY,
	                                                        description VARCHAR NOT NULL DEFAULT '',
	                                                        epoch UBIGINT NOT NULL DEFAULT 0,
	                                                      );

	                                                      CREATE TABLE node (
	                                                        address BLOB NOT NULL,
	                                                        database_id VARCHAR NOT NULL,
	                                                        role INTEGER NOT NULL,
	                                                        is_leader BOOL NOT NULL DEFAULT FALSE,
	                                                        version VARCHAR NOT NULL DEFAULT '',
	                                                        client_api_addr BLOB NOT NULL DEFAULT ''::BLOB,
	                                                        replication_addr BLOB NOT NULL,
	                                                        FOREIGN KEY (database_id) REFERENCES database (id)
	                                                      );

	                                                      CREATE UNIQUE INDEX node_id ON node (database_id, address);
	                                                      CREATE INDEX node_database ON node (database_id);

	                                                      INSERT INTO database (id) VALUES ('{Database.MainDatabaseId}');
	                                                      INSERT INTO metadata VALUES ({LatestVersion}, 0, 0);
	                                               """;

	/// <summary>
	/// Initializes the databases with the default schema.
	/// </summary>
	public void Initialize() {
		using (_pool.Rent(out var connection)) {
			using var transaction = connection.BeginTransaction();
			connection.ExecuteAdHocNonQuery(LatestSchema, multipleStatements: true);
			transaction.CommitOnDispose();
		}
	}

	private static void PerformMigration(DuckDBAdvancedConnection connection,
		int baseVersion,
		int targetVersion,
		IReadOnlyDictionary<int, Action<DuckDBAdvancedConnection>> actions) {

		// Use transaction for each transition to avoid growth of DuckDB WAL
		for (baseVersion += 1; baseVersion <= targetVersion; baseVersion++) {
			DoUpgrade(connection, actions, baseVersion);
		}

		static void DoUpgrade(
			DuckDBAdvancedConnection connection,
			IReadOnlyDictionary<int, Action<DuckDBAdvancedConnection>> actions,
			int targetVersion) {
			using var transaction = connection.BeginTransaction();
			if (actions.TryGetValue(targetVersion, out var action)) {
				action.Invoke(connection);
			}

			// update version
			connection.ExecuteNonQuery<int, UpdateVersionQuery>(targetVersion);
			transaction.CommitOnDispose();
		}
	}

	private static Metadata LoadMetadata(DuckDBAdvancedConnection connection)
		=> connection
			.ExecuteQuery<Metadata, MetadataQuery>()
			.FirstOrDefault()
			.ValueOrDefault;

	// This method MUST NOT be executed concurrently
	private void Update<TCommand>(TCommand command, in CommandInfo info)
		where TCommand : struct, IConsumer<DuckDBAdvancedConnection>, allows ref struct {
		using (var transaction = _writeConnection.BeginTransaction()) {
			command.Invoke(_writeConnection);

			_writeConnection.ExecuteNonQuery<CommandInfo, UpdateIndexAndTermQuery>(info);
			transaction.CommitOnDispose();
		}

		_lastCommandInfo = info;
	}

	// This method MUST NOT be executed concurrently
	private TResult Update<TCommand, TResult>(TCommand command, in CommandInfo info)
		where TCommand : struct, ISupplier<DuckDBAdvancedConnection, TResult>, allows ref struct {
		TResult result;
		using (var transaction = _writeConnection.BeginTransaction()) {
			result = command.Invoke(_writeConnection);

			_writeConnection.ExecuteNonQuery<CommandInfo, UpdateIndexAndTermQuery>(info);
			transaction.CommitOnDispose();
		}

		_lastCommandInfo = info;
		return result;
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly record struct Metadata(int Version, long LastAppliedIndex, long Term) {
		public static implicit operator CommandInfo(in Metadata metadata) => new() {
			Index = metadata.LastAppliedIndex,
			Term = metadata.Term,
		};
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly struct MetadataQuery : IQuery<Metadata> {
		public static ReadOnlySpan<byte> CommandText => "SELECT * FROM metadata;"u8;

		public static Metadata Parse(ref DataChunk.Row row)
			=> new() { Version = row.ReadInt32(), LastAppliedIndex = row.ReadInt64(), Term = row.ReadInt64() };
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly struct UpdateVersionQuery : IPreparedStatement<int> {
		public static ReadOnlySpan<byte> CommandText => "UPDATE metadata SET version=?;"u8;

		public static StatementBindingResult Bind(in int version, PreparedStatement source) => new(source) {
			version,
		};
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly struct UpdateIndexAndTermQuery : IPreparedStatement<CommandInfo> {
		public static ReadOnlySpan<byte> CommandText => "UPDATE metadata SET index = $1, term = $2;"u8;

		public static StatementBindingResult Bind(in CommandInfo args, PreparedStatement source) => new(source) {
			args.Index,
			args.Term
		};
	}
}
