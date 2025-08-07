// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.Core.Data;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;

public abstract class IndexTestBase : DuckDbIntegrationTest {
	private readonly DefaultIndexProcessor _processor;
	private protected readonly DefaultIndexReader Sut;
	protected readonly Guid InternalCorrId = Guid.NewGuid();
	protected readonly Guid CorrelationId = Guid.NewGuid();
	protected const string IndexName = DefaultIndex.Name;
	private readonly ReadIndexStub _readIndexStub = new();

	protected IndexTestBase() {
		const int commitBatchSize = 9;
		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		var inFlightRecords = new DefaultIndexInFlightRecords(new() { CommitBatchSize = commitBatchSize });

		var publisher = new FakePublisher();
		var categoryIndexProcessor = new CategoryIndexProcessor(DuckDb, publisher);
		var eventTypeIndexProcessor = new EventTypeIndexProcessor(DuckDb, publisher);
		var streamIndexProcessor =
			new StreamIndexProcessor(DuckDb, _readIndexStub.ReadIndex.IndexReader.Backend, hasher);

		_processor = new(
			DuckDb,
			inFlightRecords,
			categoryIndexProcessor,
			eventTypeIndexProcessor,
			streamIndexProcessor,
			new NoOpSecondaryIndexProgressTracker(),
			publisher
		);

		Sut = new(DuckDb, _processor, inFlightRecords, _readIndexStub.ReadIndex);
	}

	protected void IndexEvents(ResolvedEvent[] events, bool shouldCommit) {
		_readIndexStub.IndexEvents(events);

		foreach (var resolvedEvent in events) {
			_processor.Index(resolvedEvent);
		}

		if (shouldCommit)
			_processor.Commit();
	}
}
