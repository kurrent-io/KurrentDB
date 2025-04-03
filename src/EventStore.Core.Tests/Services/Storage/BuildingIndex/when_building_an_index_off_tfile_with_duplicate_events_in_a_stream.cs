// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.DataStructures;
using EventStore.Core.Helpers;
using EventStore.Core.Index;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.Util;
using EventStore.Core.Index.Hashes;
using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Caching;
using EventStore.Core.Data;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.BuildingIndex;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_building_an_index_off_tfile_with_duplicate_events_in_a_stream<TLogFormat, TStreamId>
	: DuplicateReadIndexTestScenario<TLogFormat, TStreamId> {
	private Guid _id1;
	private Guid _id2;
	private Guid _id3;
	private Guid _id4;

	private long pos0, pos1, pos2, pos3, pos4, pos5, pos6, pos7;

	public when_building_an_index_off_tfile_with_duplicate_events_in_a_stream() : base(maxEntriesInMemTable: 3) {
	}

	protected override async ValueTask SetupDB(CancellationToken token) {
		_id1 = Guid.NewGuid();
		_id2 = Guid.NewGuid();
		_id3 = Guid.NewGuid();

		_logFormat.StreamNameIndex.GetOrReserve(_logFormat.RecordFactory, "duplicate_stream", 0, out var streamId, out var streamRecord);
		if (streamRecord is not null) {
			(_, pos0) = await Writer.Write(streamRecord, token);
		}

		var expectedVersion = ExpectedVersion.NoStream;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		//stream id: duplicate_stream at version: 0
		(_, pos1) = await Writer.Write(LogRecord.Prepare(_logFormat.RecordFactory, pos0, _id1, _id1, pos0, 0, streamId, expectedVersion++,
			PrepareFlags.SingleWrite, eventTypeId, new byte[0], new byte[0], DateTime.UtcNow), token);
		(_, pos2) = await Writer.Write(new CommitLogRecord(pos1, _id1, pos0, DateTime.UtcNow, 0), token);

		//stream id: duplicate_stream at version: 1
		(_, pos3) = await Writer.Write(LogRecord.Prepare(_logFormat.RecordFactory, pos2, _id2, _id2, pos2, 0, streamId, expectedVersion++,
			PrepareFlags.SingleWrite, eventTypeId, new byte[0], new byte[0], DateTime.UtcNow), token);
		(_, pos4) = await Writer.Write(new CommitLogRecord(pos3, _id2, pos2, DateTime.UtcNow, 1), token);

		//stream id: duplicate_stream at version: 2
		(_, pos5) = await Writer.Write(LogRecord.Prepare(_logFormat.RecordFactory, pos4, _id3, _id3, pos4, 0, streamId, expectedVersion++,
			PrepareFlags.SingleWrite, eventTypeId, new byte[0], new byte[0], DateTime.UtcNow), token);
		(_, pos6) = await Writer.Write(new CommitLogRecord(pos5, _id3, pos4, DateTime.UtcNow, 2), token);
	}

	protected override async ValueTask Given(CancellationToken token) {
		_id4 = Guid.NewGuid();

		var streamId = _logFormat.StreamIds.LookupValue("duplicate_stream");
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		//stream id: duplicate_stream at version: 0 (duplicate event/index entry)
		(_, pos7) = await Writer.Write(LogRecord.Prepare(_logFormat.RecordFactory, pos6, _id4, _id4, pos6, 0, streamId, ExpectedVersion.NoStream,
			PrepareFlags.SingleWrite, eventTypeId, new byte[0], new byte[0], DateTime.UtcNow), token);
		await Writer.Write(new CommitLogRecord(pos7, _id4, pos6, DateTime.UtcNow, 0), token);
	}

	[Test]
	public async Task should_read_the_correct_last_event_number() {
		var result = await ReadIndex.GetStreamLastEventNumber("duplicate_stream", CancellationToken.None);
		Assert.AreEqual(2, result);
	}
}

public abstract class DuplicateReadIndexTestScenario<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected readonly int MaxEntriesInMemTable;
	protected readonly int MetastreamMaxCount;
	protected readonly bool PerformAdditionalCommitChecks;
	protected readonly byte IndexBitnessVersion;
	protected LogFormatAbstractor<TStreamId> _logFormat;
	protected TFChunkWriter Writer;
	protected IReadIndex<TStreamId> ReadIndex;

	private TFChunkDb _db;
	private TableIndex<TStreamId> _tableIndex;

	protected DuplicateReadIndexTestScenario(int maxEntriesInMemTable = 20, int metastreamMaxCount = 1,
		byte indexBitnessVersion = Opts.IndexBitnessVersionDefault, bool performAdditionalChecks = false) {
		Ensure.Positive(maxEntriesInMemTable, "maxEntriesInMemTable");
		MaxEntriesInMemTable = maxEntriesInMemTable;
		MetastreamMaxCount = metastreamMaxCount;
		IndexBitnessVersion = indexBitnessVersion;
		PerformAdditionalCommitChecks = performAdditionalChecks;
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		var writerCheckpoint = new InMemoryCheckpoint(0);
		var chaserCheckpoint = new InMemoryCheckpoint(0);

		var bus = new SynchronousScheduler();
		new IODispatcher(bus, bus);

		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerCheckpoint, chaserCheckpoint));

		await _db.Open();
		// create db
		Writer = new TFChunkWriter(_db);
		await Writer.Open(CancellationToken.None);
		await SetupDB(CancellationToken.None);
		await Writer.DisposeAsync();
		Writer = null;

		writerCheckpoint.Flush();
		chaserCheckpoint.Write(writerCheckpoint.Read());
		chaserCheckpoint.Flush();

		var readers = new ObjectPool<ITransactionFileReader>("Readers", 2, 5,
			() => new TFChunkReader(_db, _db.Config.WriterCheckpoint));
		var lowHasher = _logFormat.LowHasher;
		var highHasher = _logFormat.HighHasher;
		var emptyStreamId = _logFormat.EmptyStreamId;
		_tableIndex = new TableIndex<TStreamId>(indexDirectory, lowHasher, highHasher, emptyStreamId,
			() => new HashListMemTable(IndexBitnessVersion, MaxEntriesInMemTable * 2),
			() => new TFReaderLease(readers),
			IndexBitnessVersion,
			int.MaxValue,
			Constants.PTableMaxReaderCountDefault,
			MaxEntriesInMemTable);
		_logFormat.StreamNamesProvider.SetTableIndex(_tableIndex);

		var readIndex = new ReadIndex<TStreamId>(new NoopPublisher(),
			readers,
			_tableIndex,
			_logFormat.StreamNameIndexConfirmer,
			_logFormat.StreamIds,
			_logFormat.StreamNamesProvider,
			_logFormat.EmptyStreamId,
			_logFormat.StreamIdValidator,
			_logFormat.StreamIdSizer,
			_logFormat.StreamExistenceFilter,
			_logFormat.StreamExistenceFilterReader,
			_logFormat.EventTypeIndexConfirmer,
			new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>("LastEventNumber", 100_000),
			new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>("StreamMetadata", 100_000),
			additionalCommitChecks: PerformAdditionalCommitChecks,
			metastreamMaxCount: MetastreamMaxCount,
			hashCollisionReadLimit: Opts.HashCollisionReadLimitDefault,
			skipIndexScanOnReads: Opts.SkipIndexScanOnReadsDefault,
			replicationCheckpoint: _db.Config.ReplicationCheckpoint,
			indexCheckpoint: _db.Config.IndexCheckpoint,
			indexStatusTracker: new IndexStatusTracker.NoOp(),
			indexTracker: new IndexTracker.NoOp(),
			cacheTracker: new CacheHitsMissesTracker.NoOp());


		await readIndex.IndexCommitter.Init(chaserCheckpoint.Read(), CancellationToken.None);
		ReadIndex = readIndex;

		_tableIndex.Close(false);

		Writer = new TFChunkWriter(_db);
		await Writer.Open(CancellationToken.None);
		await Given(CancellationToken.None);
		await Writer.DisposeAsync();
		Writer = null;

		writerCheckpoint.Flush();
		chaserCheckpoint.Write(writerCheckpoint.Read());
		chaserCheckpoint.Flush();

		_tableIndex = new TableIndex<TStreamId>(indexDirectory, lowHasher, highHasher, emptyStreamId,
			() => new HashListMemTable(IndexBitnessVersion, MaxEntriesInMemTable * 2),
			() => new TFReaderLease(readers),
			IndexBitnessVersion,
			int.MaxValue,
			Constants.PTableMaxReaderCountDefault,
			MaxEntriesInMemTable);

		readIndex = new ReadIndex<TStreamId>(new NoopPublisher(),
			readers,
			_tableIndex,
			_logFormat.StreamNameIndexConfirmer,
			_logFormat.StreamIds,
			_logFormat.StreamNamesProvider,
			_logFormat.EmptyStreamId,
			_logFormat.StreamIdValidator,
			_logFormat.StreamIdSizer,
			_logFormat.StreamExistenceFilter,
			_logFormat.StreamExistenceFilterReader,
			_logFormat.EventTypeIndexConfirmer,
			new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>("LastEventNumber", 100_000),
			new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>("StreamMetadata", 100_000),
			additionalCommitChecks: PerformAdditionalCommitChecks,
			metastreamMaxCount: MetastreamMaxCount,
			hashCollisionReadLimit: Opts.HashCollisionReadLimitDefault,
			skipIndexScanOnReads: Opts.SkipIndexScanOnReadsDefault,
			replicationCheckpoint: _db.Config.ReplicationCheckpoint,
			indexCheckpoint: _db.Config.IndexCheckpoint,
			indexStatusTracker: new IndexStatusTracker.NoOp(),
			indexTracker: new IndexTracker.NoOp(),
			cacheTracker: new CacheHitsMissesTracker.NoOp());

		await readIndex.IndexCommitter.Init(chaserCheckpoint.Read(), CancellationToken.None);
		ReadIndex = readIndex;
	}

	public override async Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		ReadIndex.Close();
		ReadIndex.Dispose();

		_tableIndex.Close();

		await _db.DisposeAsync();

		await base.TestFixtureTearDown();
	}

	protected abstract ValueTask SetupDB(CancellationToken token);

	protected abstract ValueTask Given(CancellationToken token);
}
