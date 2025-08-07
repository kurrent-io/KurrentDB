// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using Serilog;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;

public class RawDuckDbMessageBatchAppender : IMessageBatchAppender {
	private readonly int _commitSize;
	private readonly int? _checkpointSize;
	private DuckDBAppender _defaultIndexAppender;
	public long LastCommittedSequence;
	public long LastSequence;
	public long LastCheckpointedSequence;
	private static readonly ILogger Logger = Log.Logger.ForContext<RawDuckDbMessageBatchAppender>();
	private readonly Stopwatch _sw = new();
	private readonly DuckDBConnection _connection;
	private DuckDBTransaction _transaction;

	public RawDuckDbMessageBatchAppender(DuckDbDataSource dbDataSource, DuckDBTestEnvironmentOptions options) {
		_commitSize = options.CommitSize;
		_checkpointSize = options.CheckpointSize;
		dbDataSource.InitDb();

		_connection = dbDataSource.OpenConnection();
		_defaultIndexAppender = _connection.CreateAppender("idx_all");

		if (!string.IsNullOrEmpty(options.WalAutocheckpoint)) {
			using var cmd = _connection.CreateCommand();
			cmd.Transaction = _transaction;
			cmd.CommandText = $"PRAGMA wal_autocheckpoint = '{options.WalAutocheckpoint}'";
			cmd.ExecuteNonQuery();
		}

		_transaction = _connection.BeginTransaction();
	}

	public ValueTask Append(TestMessageBatch batch) {
		return AppendToDefaultIndex(batch);
	}

	public ValueTask AppendToDefaultIndex(TestMessageBatch batch) {
		foreach (var message in batch.Messages) {
			var sequence = LastSequence++;
			var logPosition = message.LogSequence;
			var eventNumber = message.StreamPosition;

			var row = _defaultIndexAppender.CreateRow();
			row.AppendValue(logPosition);
			row.AppendNullValue();
			row.AppendValue(eventNumber);
			row.AppendValue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			row.AppendNullValue();
			row.AppendValue(1); //stream.Id
			row.AppendValue(1); //eventType.Id
			row.AppendValue(1); //category.Id
			row.AppendDefault();
			row.EndRow();

			if (LastSequence < LastCommittedSequence + _commitSize) continue;

			LastCommittedSequence = LastSequence;
			try {
				_sw.Restart();

				if (_checkpointSize.HasValue &&LastSequence >= LastCheckpointedSequence + _checkpointSize) {
					using(var cmd = _connection.CreateCommand())
					{
						cmd.Transaction = _transaction;
						cmd.CommandText = "CHECKPOINT;";
						cmd.ExecuteNonQuery();
					}

					LastCheckpointedSequence = LastSequence;
				}

				_transaction.Commit();
				_transaction.Dispose();

				_defaultIndexAppender.Dispose();
				_defaultIndexAppender = _connection.CreateAppender("idx_all");
				_transaction = _connection.BeginTransaction();
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
		_transaction.Dispose();
		_connection.Dispose();

		return ValueTask.CompletedTask;
	}
}
