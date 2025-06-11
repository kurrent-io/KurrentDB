// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;

public class RawQuackMessageBatchAppender : IMessageBatchAppender {
	private readonly DuckDbDataSource _dbDataSource;
	private readonly int _commitSize;
	private Appender _defaultIndexAppender;
	public long LastCommittedSequence;
	public long LastSequence;
	private static readonly ILogger Logger = Log.Logger.ForContext<RawDuckDbMessageBatchAppender>();
	private readonly Stopwatch _sw = new();

	public RawQuackMessageBatchAppender(DuckDbDataSource dbDataSource, DuckDBTestEnvironmentOptions options) {
		_dbDataSource = dbDataSource;
		_commitSize = options.CommitSize;
		_dbDataSource.InitDb();

		var connection = _dbDataSource.OpenNewConnection();
		_defaultIndexAppender = new Appender(connection, "idx_all"u8);

		if (!string.IsNullOrEmpty(options.WalAutocheckpoint)) {
			using var cmd = connection.CreateCommand();
			cmd.CommandText = $"PRAGMA wal_autocheckpoint = '{options.WalAutocheckpoint}'";
			cmd.ExecuteNonQuery();
		}
	}

	public ValueTask Append(MessageBatch batch) {
		return AppendToDefaultIndex(batch);
	}

	public ValueTask AppendToDefaultIndex(MessageBatch batch) {
		foreach (var batchMessage in batch.Messages) {
			var sequence = LastSequence++;
			var logPosition = sequence; //resolvedEvent.Event.LogPosition;
			var eventNumber = sequence;//resolvedEvent.Event.EventNumber;

			using (var row = _defaultIndexAppender.CreateRow()) {
				row.Append(sequence);
				row.Append(eventNumber);
				row.Append(logPosition);
				row.Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
				row.Append(1); //stream.Id
				row.Append(1); //eventType.Id);
				row.Append(1); //eventType.Sequence);
				row.Append(1); //category.Id);
				row.Append(1); //category.Sequence);
			}

			if (LastSequence < LastCommittedSequence + _commitSize) continue;

			LastCommittedSequence = LastSequence;
			try {
				_sw.Restart();
				_defaultIndexAppender.Flush();
				_sw.Stop();
				Console.WriteLine($"Committed {_commitSize} records to index at seq {LastSequence} ({ _sw.ElapsedMilliseconds} ms)");
				Logger.Debug("Committed {Count} records to index at seq {Seq} ({Took} ms)", _commitSize, LastSequence, _sw.ElapsedMilliseconds);
			} catch (Exception e) {
				Logger.Error(e, "Failed to commit {Count} records to index at sequence {Seq}", _commitSize, LastSequence);
				throw;
			}
		}

		return ValueTask.CompletedTask;
	}
}
