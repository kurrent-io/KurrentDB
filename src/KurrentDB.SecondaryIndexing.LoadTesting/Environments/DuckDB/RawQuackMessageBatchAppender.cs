// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using Serilog;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;

public class RawQuackMessageBatchAppender : IMessageBatchAppender {
	readonly DuckDBConnectionPool _db;
	readonly int _commitSize;
	readonly Stopwatch _sw = new();

	Appender _defaultIndexAppender;

	static readonly ILogger Logger = Log.Logger.ForContext<RawQuackMessageBatchAppender>();

	public long LastCommittedSequence;
	public long LastSequence;

	public RawQuackMessageBatchAppender(DuckDBConnectionPool db, DuckDbTestEnvironmentOptions options) {
		_db = db;
		_commitSize = options.CommitSize;
		var schema = new IndexingDbSchema(db);
		schema.CreateSchema();

		using var connection = _db.Open();
		_defaultIndexAppender = new(connection, "idx_all"u8);

		if (!string.IsNullOrEmpty(options.WalAutoCheckpoint)) {
			using var cmd = connection.CreateCommand();
			cmd.CommandText = $"PRAGMA wal_autocheckpoint = '{options.WalAutoCheckpoint}'";
			cmd.ExecuteNonQuery();
		}
	}

	public ValueTask Append(TestMessageBatch batch) {
		return AppendToDefaultIndex(batch);
	}

	public ValueTask AppendToDefaultIndex(TestMessageBatch batch) {
		foreach (var (eventNumber, logPosition, _, _, _) in batch.Messages) {
			LastSequence++;

			using (var row = _defaultIndexAppender.CreateRow()) {
				row.Append(logPosition);
				row.AppendDefault();
				row.Append(eventNumber);
				row.Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
				row.AppendDefault();
				row.Append(1); //stream.Id
				row.Append(1); //eventType.Id
				row.Append(1); //category.Id
				row.AppendDefault();
			}

			if (LastSequence < LastCommittedSequence + _commitSize) continue;

			LastCommittedSequence = LastSequence;
			try {
				_sw.Restart();
				_defaultIndexAppender.Flush();
				_sw.Stop();
				Logger.Debug("Committed {Count} records to index at seq {Seq} ({Took} ms)", _commitSize, LastSequence, _sw.ElapsedMilliseconds);
			} catch (Exception e) {
				Logger.Error(e, "Failed to commit {Count} records to index at sequence {Seq}", _commitSize, LastSequence);
				throw;
			}
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync() {
		_defaultIndexAppender.Dispose();
		return ValueTask.CompletedTask;
	}
}
