// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.Indexes;

public class IndexMessageBatchAppender : IMessageBatchAppender {
	private readonly int _commitSize;
	private long _indexedCount;
	private readonly DefaultIndexProcessor _processor;

	public IndexMessageBatchAppender(DuckDbDataSource dbDataSource, int commitSize) {
		_commitSize = commitSize;
		var reader = ReadIndexStub.Build();
		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		var inflightRecordsCache =
			new DefaultIndexInFlightRecords(new SecondaryIndexingPluginOptions { CommitBatchSize = commitSize });

		var publisher = new FakePublisher();
		var categoryIndexProcessor = new CategoryIndexProcessor(dbDataSource, publisher);
		var eventTypeIndexProcessor = new EventTypeIndexProcessor(dbDataSource, publisher);
		var streamIndexProcessor = new StreamIndexProcessor(dbDataSource, reader.IndexReader.Backend, hasher);

		_processor = new DefaultIndexProcessor(
			dbDataSource,
			inflightRecordsCache,
			categoryIndexProcessor,
			eventTypeIndexProcessor,
			streamIndexProcessor,
			new NoOpSecondaryIndexProgressTracker(), // TODO: Use the real one with metrics
			publisher
		);
	}

	public ValueTask Append(TestMessageBatch batch) {
		foreach (var resolvedEvent in batch.ToResolvedEvents()) {
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
