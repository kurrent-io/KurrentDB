// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Metrics;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Util;
using EventStore.LogCommon;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage;

public abstract class ReadIndexTestScenario<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected readonly int MaxEntriesInMemTable;
	protected readonly int StreamInfoCacheCapacity;
	protected readonly long MetastreamMaxCount;
	protected readonly bool PerformAdditionalCommitChecks;
	protected readonly byte IndexBitnessVersion;
	protected LogFormatAbstractor<TStreamId> _logFormat;
	protected IRecordFactory<TStreamId> _recordFactory;
	protected INameIndex<TStreamId> _streamNameIndex;
	protected INameIndex<TStreamId> _eventTypeIndex;
	protected ITableIndex<TStreamId> TableIndex;
	protected IReadIndex<TStreamId> ReadIndex;

	protected IHasher<TStreamId> LowHasher { get; private set; }
	protected IHasher<TStreamId> HighHasher { get; private set; }
	protected ILongHasher<TStreamId> Hasher { get; private set; }

	protected TFChunkDb Db;
	protected TFChunkWriter Writer;
	protected ICheckpoint WriterCheckpoint;
	protected ICheckpoint ChaserCheckpoint;

	private readonly int _chunkSize;
	private TFChunkScavenger<TStreamId> _scavenger;
	private bool _scavenge;
	private bool _completeLastChunkOnScavenge;
	private bool _mergeChunks;
	private bool _scavengeIndex;

	protected ReadIndexTestScenario(
		int maxEntriesInMemTable = 20,
		long metastreamMaxCount = 1,
		int streamInfoCacheCapacity = 0,
		byte indexBitnessVersion = Opts.IndexBitnessVersionDefault,
		bool performAdditionalChecks = true,
		int chunkSize = 10_000,
		IHasher<TStreamId> lowHasher = null,
		IHasher<TStreamId> highHasher = null) {

		Ensure.Positive(maxEntriesInMemTable, "maxEntriesInMemTable");
		MaxEntriesInMemTable = maxEntriesInMemTable;
		StreamInfoCacheCapacity = streamInfoCacheCapacity;
		MetastreamMaxCount = metastreamMaxCount;
		IndexBitnessVersion = indexBitnessVersion;
		PerformAdditionalCommitChecks = performAdditionalChecks;
		LowHasher = lowHasher;
		HighHasher = highHasher;
		_chunkSize = chunkSize;
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});
		_recordFactory = _logFormat.RecordFactory;
		_streamNameIndex = _logFormat.StreamNameIndex;
		_eventTypeIndex = _logFormat.EventTypeIndex;
		LowHasher ??= _logFormat.LowHasher;
		HighHasher ??= _logFormat.HighHasher;
		Hasher = new CompositeHasher<TStreamId>(LowHasher, HighHasher);

		WriterCheckpoint = new InMemoryCheckpoint(0);
		ChaserCheckpoint = new InMemoryCheckpoint(0);

		Db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, WriterCheckpoint, ChaserCheckpoint,
			replicationCheckpoint: new InMemoryCheckpoint(-1), chunkSize: _chunkSize));

		Db.Open();
		// create db
		Writer = new TFChunkWriter(Db);
		Writer.Open();
		WriteTestScenario();
		Writer.Close();
		Writer = null;

		WriterCheckpoint.Flush();
		ChaserCheckpoint.Write(WriterCheckpoint.Read());
		ChaserCheckpoint.Flush();

		var readers = new ObjectPool<ITransactionFileReader>("Readers", 2, 5,
			() => new TFChunkReader(Db, Db.Config.WriterCheckpoint));
		var emptyStreamId = _logFormat.EmptyStreamId;
		TableIndex = TransformTableIndex(new TableIndex<TStreamId>(indexDirectory, LowHasher, HighHasher, emptyStreamId,
			() => new HashListMemTable(IndexBitnessVersion, MaxEntriesInMemTable * 2),
			() => new TFReaderLease(readers),
			IndexBitnessVersion,
			int.MaxValue,
			Constants.PTableMaxReaderCountDefault,
			MaxEntriesInMemTable));
		_logFormat.StreamNamesProvider.SetTableIndex(TableIndex);

		var readIndex = new ReadIndex<TStreamId>(new NoopPublisher(),
			readers,
			TableIndex,
			_logFormat.StreamNameIndexConfirmer,
			_logFormat.StreamIds,
			_logFormat.StreamNamesProvider,
			_logFormat.EmptyStreamId,
			_logFormat.StreamIdValidator,
			_logFormat.StreamIdSizer,
			_logFormat.StreamExistenceFilter,
			_logFormat.StreamExistenceFilterReader,
			_logFormat.EventTypeIndexConfirmer,
			new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>("LastEventNumber", StreamInfoCacheCapacity),
			new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>("StreamMetadata", StreamInfoCacheCapacity),
			additionalCommitChecks: PerformAdditionalCommitChecks,
			metastreamMaxCount: MetastreamMaxCount,
			hashCollisionReadLimit: Opts.HashCollisionReadLimitDefault,
			skipIndexScanOnReads: Opts.SkipIndexScanOnReadsDefault,
			replicationCheckpoint: Db.Config.ReplicationCheckpoint,
			indexCheckpoint: Db.Config.IndexCheckpoint,
			indexStatusTracker: new IndexStatusTracker.NoOp(),
			indexTracker: new IndexTracker.NoOp(),
			cacheTracker: new CacheHitsMissesTracker.NoOp());

		readIndex.IndexCommitter.Init(ChaserCheckpoint.Read());
		ReadIndex = readIndex;

		// wait for tables to be merged
		TableIndex.WaitForBackgroundTasks(16_000);

		// scavenge must run after readIndex is built
		if (_scavenge) {
			if (_completeLastChunkOnScavenge)
				Db.Manager.GetChunk(Db.Manager.ChunksCount - 1).Complete();
			_scavenger = new TFChunkScavenger<TStreamId>(Serilog.Log.Logger, Db, new FakeTFScavengerLog(), TableIndex, ReadIndex, _logFormat.Metastreams);
			await _scavenger.Scavenge(alwaysKeepScavenged: true, mergeChunks: _mergeChunks,
				scavengeIndex: _scavengeIndex);
		}
	}

	public override Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		ReadIndex?.Close();
		ReadIndex?.Dispose();

		TableIndex?.Close();

		Db?.Close();
		Db?.Dispose();

		return base.TestFixtureTearDown();
	}

	protected virtual ITableIndex<TStreamId> TransformTableIndex(ITableIndex<TStreamId> tableIndex) {
		return tableIndex;
	}

	protected abstract void WriteTestScenario();

	protected void GetOrReserve(string eventStreamName, out TStreamId eventStreamId, out long newPos) {
		newPos = Writer.Position;
		_streamNameIndex.GetOrReserve(_logFormat.RecordFactory, eventStreamName, newPos, out eventStreamId, out var streamRecord);
		if (streamRecord != null) {
			Writer.Write(streamRecord, out newPos);
		}
	}

	protected void GetOrReserveEventType(string eventType, out TStreamId eventTypeId, out long newPos) {
		newPos = Writer.Position;
		_eventTypeIndex.GetOrReserveEventType(_logFormat.RecordFactory, eventType, newPos, out eventTypeId, out var eventTypeRecord);
		if (eventTypeRecord != null) {
			Writer.Write(eventTypeRecord, out newPos);
		}
	}

	protected EventRecord WriteSingleEvent(string eventStreamName,
		long eventNumber,
		string data,
		DateTime? timestamp = null,
		Guid eventId = default(Guid),
		bool retryOnFail = false,
		string eventType = "some-type") {
		GetOrReserve(eventStreamName, out var eventStreamId, out var pos);
		GetOrReserveEventType(eventType, out var eventTypeId, out pos);
		var prepare = LogRecord.SingleWrite(_recordFactory, pos,
			eventId == default(Guid) ? Guid.NewGuid() : eventId,
			Guid.NewGuid(),
			eventStreamId,
			eventNumber - 1,
			eventTypeId,
			Helper.UTF8NoBom.GetBytes(data),
			null,
			timestamp);

		if (!retryOnFail) {
			Assert.IsTrue(Writer.Write(prepare, out pos));
		} else {
			long firstPos = prepare.LogPosition;
			if (!Writer.Write(prepare, out pos)) {
				prepare = LogRecord.SingleWrite(_recordFactory, pos,
					prepare.CorrelationId,
					prepare.EventId,
					prepare.EventStreamId,
					prepare.ExpectedVersion,
					prepare.EventType,
					prepare.Data,
					prepare.Metadata,
					prepare.TimeStamp);
				if (!Writer.Write(prepare, out pos))
					Assert.Fail("Second write try failed when first writing prepare at {0}, then at {1}.", firstPos,
						prepare.LogPosition);
			}
		}


		var commit = LogRecord.Commit(Writer.Position, prepare.CorrelationId, prepare.LogPosition,
			eventNumber);
		if (!retryOnFail) {
			Assert.IsTrue(Writer.Write(commit, out pos));
		} else {
			var firstPos = commit.LogPosition;
			if (!Writer.Write(commit, out pos)) {
				commit = LogRecord.Commit(pos, prepare.CorrelationId, prepare.LogPosition,
					eventNumber);
				if (!Writer.Write(commit, out pos))
					Assert.Fail("Second write try failed when first writing prepare at {0}, then at {1}.", firstPos,
						prepare.LogPosition);
			}
		}
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		var eventRecord = new EventRecord(eventNumber, prepare, eventStreamName, eventType);
		return eventRecord;
	}

	protected EventRecord WriteStreamMetadata(string eventStreamName, long eventNumber, string metadata,
		DateTime? timestamp = null) {
		GetOrReserve(SystemStreams.MetastreamOf(eventStreamName), out var eventStreamId, out var pos);
		GetOrReserveEventType(SystemEventTypes.StreamMetadata, out var eventTypeId, out pos);
		var prepare = LogRecord.SingleWrite(_recordFactory, pos,
			Guid.NewGuid(),
			Guid.NewGuid(),
			eventStreamId,
			eventNumber - 1,
			eventTypeId,
			Helper.UTF8NoBom.GetBytes(metadata),
			null,
			timestamp ?? DateTime.UtcNow,
			PrepareFlags.IsJson);
		Assert.IsTrue(Writer.Write(prepare, out pos));

		var commit = LogRecord.Commit(Writer.Position, prepare.CorrelationId, prepare.LogPosition,
			eventNumber);
		Assert.IsTrue(Writer.Write(commit, out pos));
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		var eventRecord = new EventRecord(eventNumber, prepare, SystemStreams.MetastreamOf(eventStreamName), SystemEventTypes.StreamMetadata);
		return eventRecord;
	}

	protected EventRecord WriteTransactionBegin(string eventStreamName, long expectedVersion, long eventNumber,
		string eventData) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		GetOrReserve(eventStreamName, out var eventStreamId, out var pos);
		GetOrReserveEventType("some-type", out var eventTypeId, out pos);
		var prepare = LogRecord.Prepare(_recordFactory, pos,
			Guid.NewGuid(),
			Guid.NewGuid(),
			Writer.Position,
			0,
			eventStreamId,
			expectedVersion,
			PrepareFlags.Data | PrepareFlags.TransactionBegin,
			eventTypeId,
			Helper.UTF8NoBom.GetBytes(eventData),
			null);
		Assert.IsTrue(Writer.Write(prepare, out pos));
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		return new EventRecord(eventNumber, prepare, eventStreamName, "some-type");
	}

	protected IPrepareLogRecord<TStreamId> WriteTransactionBegin(string eventStreamName, long expectedVersion) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		GetOrReserve(eventStreamName, out var eventStreamId, out var pos);
		var prepare = LogRecord.TransactionBegin(_recordFactory, pos, Guid.NewGuid(), eventStreamId,
			expectedVersion);
		Assert.IsTrue(Writer.Write(prepare, out pos));
		return prepare;
	}

	protected EventRecord WriteTransactionEvent(Guid correlationId,
		long transactionPos,
		int transactionOffset,
		string eventStreamName,
		long eventNumber,
		string eventData,
		PrepareFlags flags,
		bool retryOnFail = false,
		string eventType = "some-type") {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();

		GetOrReserve(eventStreamName, out var eventStreamId, out var pos);
		GetOrReserveEventType(eventType, out var eventTypeId, out pos);

		var prepare = LogRecord.Prepare(_recordFactory, pos,
			correlationId,
			Guid.NewGuid(),
			transactionPos,
			transactionOffset,
			eventStreamId,
			ExpectedVersion.Any,
			flags,
			eventTypeId,
			Helper.UTF8NoBom.GetBytes(eventData),
			null);

		if (retryOnFail) {
			long firstPos = prepare.LogPosition;
			long newPos;
			if (!Writer.Write(prepare, out newPos)) {
				var tPos = prepare.TransactionPosition == prepare.LogPosition
					? newPos
					: prepare.TransactionPosition;
				prepare = prepare.CopyForRetry(
					logPosition: newPos,
					transactionPosition: tPos);
				if (!Writer.Write(prepare, out newPos))
					Assert.Fail("Second write try failed when first writing prepare at {0}, then at {1}.", firstPos,
						prepare.LogPosition);
			}

			Assert.AreEqual(eventStreamId, prepare.EventStreamId);
			return new EventRecord(eventNumber, prepare, eventStreamName, eventType);
		}

		Assert.IsTrue(Writer.Write(prepare, out pos));

		Assert.AreEqual(eventStreamId, prepare.EventStreamId);
		return new EventRecord(eventNumber, prepare, eventStreamName, eventType);
	}

	protected IPrepareLogRecord<TStreamId> WriteTransactionEnd(Guid correlationId, long transactionId, string eventStreamName) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		GetOrReserve(eventStreamName, out var eventStreamId, out _);
		return WriteTransactionEnd(correlationId, transactionId, eventStreamId);
	}

	protected IPrepareLogRecord<TStreamId> WriteTransactionEnd(Guid correlationId, long transactionId, TStreamId eventStreamId) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var prepare = LogRecord.TransactionEnd(_recordFactory, Writer.Position,
			correlationId,
			Guid.NewGuid(),
			transactionId,
			eventStreamId);
		long pos;
		Assert.IsTrue(Writer.Write(prepare, out pos));
		return prepare;
	}

	protected IPrepareLogRecord<TStreamId> WritePrepare(string eventStreamName,
		long expectedVersion,
		Guid eventId = default(Guid),
		string eventType = null,
		string data = null,
		PrepareFlags additionalFlags = PrepareFlags.None) {
		GetOrReserve(eventStreamName, out var eventStreamId, out var pos);
		GetOrReserveEventType(eventType.IsEmptyString() ? "some-type" : eventType, out var eventTypeId, out pos);
		var prepare = LogRecord.SingleWrite(_recordFactory, pos,
			Guid.NewGuid(),
			eventId == default(Guid) ? Guid.NewGuid() : eventId,
			eventStreamId,
			expectedVersion,
			eventTypeId,
			data.IsEmptyString() ? LogRecord.NoData : Helper.UTF8NoBom.GetBytes(data),
			LogRecord.NoData,
			DateTime.UtcNow,
			additionalFlags);
		Assert.IsTrue(Writer.Write(prepare, out pos));

		return prepare;
	}

	protected CommitLogRecord WriteCommit(long preparePos, string eventStreamName, long eventNumber) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var commit = LogRecord.Commit(Writer.Position, Guid.NewGuid(), preparePos, eventNumber);
		long pos;
		Assert.IsTrue(Writer.Write(commit, out pos));
		return commit;
	}

	protected long WriteCommit(Guid correlationId, long transactionId, string eventStreamName, long eventNumber) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		GetOrReserve(eventStreamName, out var eventStreamId, out _);
		return WriteCommit(correlationId, transactionId, eventStreamId, eventNumber);
	}

	protected long WriteCommit(Guid correlationId, long transactionId, TStreamId eventStreamId, long eventNumber) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var commit = LogRecord.Commit(Writer.Position, correlationId, transactionId, eventNumber);
		long pos;
		Assert.IsTrue(Writer.Write(commit, out pos));
		return commit.LogPosition;
	}

	protected EventRecord WriteDelete(string eventStreamName) {
		GetOrReserve(eventStreamName, out var eventStreamId, out var pos);
		GetOrReserveEventType(SystemEventTypes.StreamDeleted, out var streamDeletedEventTypeId, out pos);
		var prepare = LogRecord.DeleteTombstone(_recordFactory, pos,
			Guid.NewGuid(), Guid.NewGuid(), eventStreamId, streamDeletedEventTypeId, EventNumber.DeletedStream - 1);
		Assert.IsTrue(Writer.Write(prepare, out pos));
		var commit = LogRecord.Commit(Writer.Position,
			prepare.CorrelationId,
			prepare.LogPosition,
			EventNumber.DeletedStream);
		Assert.IsTrue(Writer.Write(commit, out pos));
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		return new EventRecord(EventNumber.DeletedStream, prepare, eventStreamName, SystemEventTypes.StreamDeleted);
	}

	protected IPrepareLogRecord<TStreamId> WriteDeletePrepare(string eventStreamName) {
		GetOrReserve(eventStreamName, out var eventStreamId, out var pos);
		GetOrReserveEventType(SystemEventTypes.StreamDeleted, out var streamDeletedEventTypeId, out pos);
		var prepare = LogRecord.DeleteTombstone(_recordFactory, pos,
			Guid.NewGuid(), Guid.NewGuid(), eventStreamId, streamDeletedEventTypeId, ExpectedVersion.Any);
		Assert.IsTrue(Writer.Write(prepare, out pos));

		return prepare;
	}

	protected CommitLogRecord WriteDeleteCommit(IPrepareLogRecord prepare) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		long pos;
		var commit = LogRecord.Commit(Writer.Position,
			prepare.CorrelationId,
			prepare.LogPosition,
			EventNumber.DeletedStream);
		Assert.IsTrue(Writer.Write(commit, out pos));

		return commit;
	}

	// This is LogV2 specific
	protected PrepareLogRecord WriteSingleEventWithLogVersion0(Guid id, string streamId, long position,
		long expectedVersion, PrepareFlags? flags = null) {
		if (!flags.HasValue) {
			flags = PrepareFlags.SingleWrite;
		}

		long pos;
		var record = new PrepareLogRecord(position, id, id, position, 0, streamId, null, expectedVersion, DateTime.UtcNow,
			flags.Value, "type", null, new byte[10], new byte[0], LogRecordVersion.LogRecordV0);
		Writer.Write(record, out pos);
		Writer.Write(
			new CommitLogRecord(pos, id, position, DateTime.UtcNow, expectedVersion, LogRecordVersion.LogRecordV0),
			out pos);
		return record;
	}

	protected TFPos GetBackwardReadPos() {
		var pos = new TFPos(WriterCheckpoint.ReadNonFlushed(), WriterCheckpoint.ReadNonFlushed());
		return pos;
	}

	protected void Scavenge(bool completeLast, bool mergeChunks, bool scavengeIndex = true) {
		if (_scavenge)
			throw new InvalidOperationException("Scavenge can be executed only once in ReadIndexTestScenario");
		_scavenge = true;
		_completeLastChunkOnScavenge = completeLast;
		_mergeChunks = mergeChunks;
		_scavengeIndex = scavengeIndex;
	}
}
