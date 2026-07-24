// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

/// <summary>
/// Represents Kontroller database snapshot.
/// </summary>
/// <remarks>
/// In-memory snapshot uses DuckDB in-memory mode to perform updates and process queries. Thus, we're assuming
/// that the Kontroller database should not be very large (tens of megabytes). Every log entry is applied to
/// the in-memory state. When we need to create a persistent snapshot, COPY FROM DATABASE statement is used to
/// dump in-memory state to the disk as DuckDB database file. However, we don't want to block Apply loop with
/// the heavyweight dump process. So we can start COPY statement in the background while appending new log entries.
/// This concurrency model is preserved by DuckDB 1.5 and later.
/// </remarks>
internal sealed partial class ClusterState : Disposable {
	private readonly DuckDBConnectionPool _pool;
	private readonly DuckDBAdvancedConnection _writeConnection;
	private CommandInfo _lastCommandInfo;

	public ClusterState(int connectionPoolCapacity) {
		// For every instance of this class, we have a separated instance of the in-memory database
		_writeConnection = new DuckDBAdvancedConnection { ConnectionString = "DataSource=:memory:" };
		_writeConnection.Open();
		_pool = new(_writeConnection) { Capacity = connectionPoolCapacity };
		_referenceCounter = 1U;
	}

	public ref readonly CommandInfo LastAppliedCommand => ref _lastCommandInfo;

	public DuckDBConnectionPool.Scope RentConnection(out DuckDBAdvancedConnection connection)
		=> _pool.Rent(out connection);

	/// <summary>
	/// Reclaims memory related to deleted rows.
	/// </summary>
	public void ReclaimGarbage() {
		ReadOnlySpan<byte> command = "FORCE CHECKPOINT;"u8;

		using (_pool.Rent(out var connection)) {
			connection.ExecuteAdHocNonQuery(command);
		}
	}

	public void SaveToFile(string fileName) {
		var command = $"""
		               ATTACH '{fileName}' AS snapshot;
		               COPY FROM DATABASE memory TO snapshot;
		               DETACH snapshot;
		               """;
		using (_pool.Rent(out var connection)) {
			connection.ExecuteAdHocNonQuery(command, multipleStatements: true);
		}
	}

	public void LoadFromFile(string fileName, int targetVersion = LatestVersion) {
		var command = $"""
		               ATTACH '{fileName}' AS snapshot;
		               COPY FROM DATABASE snapshot TO memory;
		               DETACH snapshot;
		               """;
		using (_pool.Rent(out var connection)) {
			connection.ExecuteAdHocNonQuery(command, multipleStatements: true);

			var metadata = LoadMetadata(connection);
			_lastCommandInfo = metadata;
			PerformMigration(connection, metadata.Version, targetVersion, MigrationActions);
		}
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_pool.Dispose();
			_writeConnection.Dispose();
		}

		base.Dispose(disposing);
	}
}
