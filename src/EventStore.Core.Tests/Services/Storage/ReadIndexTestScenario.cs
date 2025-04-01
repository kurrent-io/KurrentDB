// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Caching;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using EventStore.Core.Util;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogV3;
using EventStore.Core.Metrics;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.LogCommon;

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

		await Db.Open();
		// create db
		Writer = new TFChunkWriter(Db);
		await Writer.Open(CancellationToken.None);
		await WriteTestScenario(CancellationToken.None);
		await Writer.DisposeAsync();
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

		await readIndex.IndexCommitter.Init(ChaserCheckpoint.Read(), CancellationToken.None);
		ReadIndex = readIndex;

		// wait for tables to be merged
		TableIndex.WaitForBackgroundTasks(16_000);

		// scavenge must run after readIndex is built
		if (_scavenge) {
			if (_completeLastChunkOnScavenge)
				await (await Db.Manager.GetInitializedChunk(Db.Manager.ChunksCount - 1, CancellationToken.None))
					.Complete(CancellationToken.None);
			_scavenger = new TFChunkScavenger<TStreamId>(Serilog.Log.Logger, Db, new FakeTFScavengerLog(), TableIndex,
				ReadIndex, _logFormat.Metastreams);
			await _scavenger.Scavenge(alwaysKeepScavenged: true, mergeChunks: _mergeChunks,
				scavengeIndex: _scavengeIndex);
		}
	}

	public override async Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		ReadIndex?.Close();
		ReadIndex?.Dispose();

		TableIndex?.Close();

		await (Db?.DisposeAsync() ?? ValueTask.CompletedTask);
		await base.TestFixtureTearDown();
	}

	protected virtual ITableIndex<TStreamId> TransformTableIndex(ITableIndex<TStreamId> tableIndex) {
		return tableIndex;
	}

	protected virtual ValueTask WriteTestScenario(CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled(token) : ValueTask.CompletedTask;

	protected async ValueTask<(TStreamId, long)> GetOrReserve(string eventStreamName, CancellationToken token) {
		var newPos = Writer.Position;
		_streamNameIndex.GetOrReserve(_logFormat.RecordFactory, eventStreamName, newPos, out var eventStreamId, out var streamRecord);
		if (streamRecord is not null) {
			(_, newPos) = await Writer.Write(streamRecord, token);
		}

		return (eventStreamId, newPos);
	}

	protected async ValueTask<(TStreamId, long)> GetOrReserveEventType(string eventType, CancellationToken token) {
		var newPos = Writer.Position;
		_eventTypeIndex.GetOrReserveEventType(_logFormat.RecordFactory, eventType, newPos, out var eventTypeId, out var eventTypeRecord);
		if (eventTypeRecord is not null) {
			(_, newPos) = await Writer.Write(eventTypeRecord, token);
		}

		return (eventTypeId, newPos);
	}

	protected async ValueTask<EventRecord> WriteSingleEvent(string eventStreamName,
		long eventNumber,
		string data,
		DateTime? timestamp = null,
		Guid eventId = default,
		bool retryOnFail = false,
		string eventType = "some-type",
		CancellationToken token = default) {
		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		var (eventTypeId, pos) = await GetOrReserveEventType(eventType, token);

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
			Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));
		} else {
			long firstPos = prepare.LogPosition;
			(var success, pos) = await Writer.Write(prepare, token);
			if (!success) {
				prepare = LogRecord.SingleWrite(_recordFactory, pos,
					prepare.CorrelationId,
					prepare.EventId,
					prepare.EventStreamId,
					prepare.ExpectedVersion,
					prepare.EventType,
					prepare.Data,
					prepare.Metadata,
					prepare.TimeStamp);

				if (await Writer.Write(prepare, token) is (false, _))
					Assert.Fail("Second write try failed when first writing prepare at {0}, then at {1}.", firstPos,
						prepare.LogPosition);
			}
		}


		var commit = LogRecord.Commit(Writer.Position, prepare.CorrelationId, prepare.LogPosition,
			eventNumber);
		if (!retryOnFail) {
			Assert.IsTrue(await Writer.Write(commit, token) is (true, _));
		} else {
			var firstPos = commit.LogPosition;
			(var success, pos) = await Writer.Write(commit, token);
			if (!success) {
				commit = LogRecord.Commit(pos, prepare.CorrelationId, prepare.LogPosition,
					eventNumber);
				if (await Writer.Write(commit, token) is (false, _))
					Assert.Fail("Second write try failed when first writing prepare at {0}, then at {1}.", firstPos,
						prepare.LogPosition);
			}
		}
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		var eventRecord = new EventRecord(eventNumber, prepare, eventStreamName, eventType);
		return eventRecord;
	}

	protected async ValueTask<EventRecord> WriteStreamMetadata(string eventStreamName, long eventNumber, string metadata,
		DateTime? timestamp = null,
		CancellationToken token = default) {
		var (eventStreamId, _) = await GetOrReserve(SystemStreams.MetastreamOf(eventStreamName), token);
		var ( eventTypeId, pos) = await GetOrReserveEventType(SystemEventTypes.StreamMetadata, token);
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
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));

		var commit = LogRecord.Commit(Writer.Position, prepare.CorrelationId, prepare.LogPosition,
			eventNumber);
		Assert.IsTrue(await Writer.Write(commit, token) is (true, _));
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		var eventRecord = new EventRecord(eventNumber, prepare, SystemStreams.MetastreamOf(eventStreamName), SystemEventTypes.StreamMetadata);
		return eventRecord;
	}

	protected async ValueTask<EventRecord> WriteTransactionBegin(string eventStreamName, long expectedVersion, long eventNumber,
		string eventData, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		var (eventTypeId, pos) = await GetOrReserveEventType("some-type", token);
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
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		return new EventRecord(eventNumber, prepare, eventStreamName, "some-type");
	}

	protected async ValueTask<IPrepareLogRecord<TStreamId>> WriteTransactionBegin(string eventStreamName, long expectedVersion, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var (eventStreamId, pos) = await GetOrReserve(eventStreamName, token);
		var prepare = LogRecord.TransactionBegin(_recordFactory, pos, Guid.NewGuid(), eventStreamId,
			expectedVersion);
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));
		return prepare;
	}

	protected async ValueTask<EventRecord> WriteTransactionEvent(Guid correlationId,
		long transactionPos,
		int transactionOffset,
		string eventStreamName,
		long eventNumber,
		string eventData,
		PrepareFlags flags,
		bool retryOnFail = false,
		string eventType = "some-type",
		CancellationToken token = default) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();

		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		var (eventTypeId, pos) = await GetOrReserveEventType(eventType, token);

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
			var (success, newPos) = await Writer.Write(prepare, token);
			if (!success) {
				var tPos = prepare.TransactionPosition == prepare.LogPosition
					? newPos
					: prepare.TransactionPosition;
				prepare = prepare.CopyForRetry(
					logPosition: newPos,
					transactionPosition: tPos);
				if (await Writer.Write(prepare, token) is (false, _))
					Assert.Fail("Second write try failed when first writing prepare at {0}, then at {1}.", firstPos,
						prepare.LogPosition);
			}

			Assert.AreEqual(eventStreamId, prepare.EventStreamId);
			return new EventRecord(eventNumber, prepare, eventStreamName, eventType);
		}

		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));

		Assert.AreEqual(eventStreamId, prepare.EventStreamId);
		return new EventRecord(eventNumber, prepare, eventStreamName, eventType);
	}

	protected async ValueTask<IPrepareLogRecord<TStreamId>> WriteTransactionEnd(Guid correlationId, long transactionId, string eventStreamName, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		return await WriteTransactionEnd(correlationId, transactionId, eventStreamId, token);
	}

	protected async ValueTask<IPrepareLogRecord<TStreamId>> WriteTransactionEnd(Guid correlationId, long transactionId, TStreamId eventStreamId, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var prepare = LogRecord.TransactionEnd(_recordFactory, Writer.Position,
			correlationId,
			Guid.NewGuid(),
			transactionId,
			eventStreamId);
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));
		return prepare;
	}

	protected async ValueTask<IPrepareLogRecord<TStreamId>> WritePrepare(string eventStreamName,
		long expectedVersion,
		Guid eventId = default,
		string eventType = null,
		string data = null,
		PrepareFlags additionalFlags = PrepareFlags.None,
		CancellationToken token = default) {
		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		var (eventTypeId, pos) = await GetOrReserveEventType(eventType.IsEmptyString() ? "some-type" : eventType, token);
		var prepare = LogRecord.SingleWrite(_recordFactory, pos,
			Guid.NewGuid(),
			eventId == default ? Guid.NewGuid() : eventId,
			eventStreamId,
			expectedVersion,
			eventTypeId,
			data.IsEmptyString() ? LogRecord.NoData : Helper.UTF8NoBom.GetBytes(data),
			LogRecord.NoData,
			DateTime.UtcNow,
			additionalFlags);
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));

		return prepare;
	}

	protected async ValueTask<CommitLogRecord> WriteCommit(long preparePos, string eventStreamName, long eventNumber, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var commit = LogRecord.Commit(Writer.Position, Guid.NewGuid(), preparePos, eventNumber);
		Assert.IsTrue(await Writer.Write(commit, token) is (true, _));
		return commit;
	}

	protected async ValueTask<long> WriteCommit(Guid correlationId, long transactionId, string eventStreamName,
		long eventNumber, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		return await WriteCommit(correlationId, transactionId, eventStreamId, eventNumber, token);
	}

	protected async ValueTask<long> WriteCommit(Guid correlationId, long transactionId, TStreamId eventStreamId, long eventNumber, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var commit = LogRecord.Commit(Writer.Position, correlationId, transactionId, eventNumber);
		Assert.IsTrue(await Writer.Write(commit, token) is (true, _));
		return commit.LogPosition;
	}

	protected async ValueTask<EventRecord> WriteDelete(string eventStreamName, CancellationToken token) {
		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		var (streamDeletedEventTypeId, pos) = await GetOrReserveEventType(SystemEventTypes.StreamDeleted, token);
		var prepare = LogRecord.DeleteTombstone(_recordFactory, pos,
			Guid.NewGuid(), Guid.NewGuid(), eventStreamId, streamDeletedEventTypeId, EventNumber.DeletedStream - 1);
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));
		var commit = LogRecord.Commit(Writer.Position,
			prepare.CorrelationId,
			prepare.LogPosition,
			EventNumber.DeletedStream);
		Assert.IsTrue(await Writer.Write(commit, token) is (true, _));
		Assert.AreEqual(eventStreamId, prepare.EventStreamId);

		return new EventRecord(EventNumber.DeletedStream, prepare, eventStreamName, SystemEventTypes.StreamDeleted);
	}

	protected async ValueTask<IPrepareLogRecord<TStreamId>> WriteDeletePrepare(string eventStreamName, CancellationToken token) {
		var (eventStreamId, _) = await GetOrReserve(eventStreamName, token);
		var (streamDeletedEventTypeId, pos) = await GetOrReserveEventType(SystemEventTypes.StreamDeleted, token);
		var prepare = LogRecord.DeleteTombstone(_recordFactory, pos,
			Guid.NewGuid(), Guid.NewGuid(), eventStreamId, streamDeletedEventTypeId, ExpectedVersion.Any);
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));

		return prepare;
	}

	protected async ValueTask<CommitLogRecord> WriteDeleteCommit(IPrepareLogRecord prepare, CancellationToken token) {
		LogFormatHelper<TLogFormat, TStreamId>.CheckIfExplicitTransactionsSupported();
		var commit = LogRecord.Commit(Writer.Position,
			prepare.CorrelationId,
			prepare.LogPosition,
			EventNumber.DeletedStream);
		Assert.IsTrue(await Writer.Write(commit, token) is (true, _));

		return commit;
	}

	// This is LogV2 specific
	protected async ValueTask<PrepareLogRecord> WriteSingleEventWithLogVersion0(Guid id, string streamId,
		long position,
		long expectedVersion, PrepareFlags? flags = null,
		CancellationToken token = default) {
		if (!flags.HasValue) {
			flags = PrepareFlags.SingleWrite;
		}

		var record = new PrepareLogRecord(position, id, id, position, 0, streamId, null, expectedVersion,
			DateTime.UtcNow,
			flags.Value, "type", null, new byte[10], new byte[0], LogRecordVersion.LogRecordV0);
		var (_, pos) = await Writer.Write(record, token);
		await Writer.Write(
			new CommitLogRecord(pos, id, position, DateTime.UtcNow, expectedVersion, LogRecordVersion.LogRecordV0),
			token);
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
