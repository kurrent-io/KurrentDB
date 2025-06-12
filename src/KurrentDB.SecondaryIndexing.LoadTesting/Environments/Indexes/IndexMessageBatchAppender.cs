// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.Indexes;

public class IndexMessageBatchAppender: IMessageBatchAppender {
	private readonly int _commitSize;
	private long _indexedCount;
	private readonly DefaultIndexProcessor _processor;

	public IndexMessageBatchAppender(DuckDbDataSource dbDataSource, int commitSize) {
		_commitSize = commitSize;
		var reader = new DummyReadIndex();
		var defaultIndex = new DefaultIndex(dbDataSource, reader, commitSize);
		_processor = new DefaultIndexProcessor(dbDataSource, defaultIndex, commitSize);
	}

	public ValueTask Append(MessageBatch batch) {
		foreach (var resolvedEvent in batch.Messages.Select(m => m.ToResolvedEvent(batch.StreamName))) {
			_processor.Index(resolvedEvent);

			if (++_indexedCount < _commitSize) continue;

			_processor.Commit();
			_indexedCount = 0;
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync() {
		_processor.Dispose();

		return ValueTask.CompletedTask;
	}
}
