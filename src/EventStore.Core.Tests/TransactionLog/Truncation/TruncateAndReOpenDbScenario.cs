// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.Util;

namespace EventStore.Core.Tests.TransactionLog.Truncation;

public abstract class TruncateAndReOpenDbScenario<TLogFormat, TStreamId> : TruncateScenario<TLogFormat, TStreamId> {
	protected TruncateAndReOpenDbScenario(int maxEntriesInMemTable = 100, int metastreamMaxCount = 1)
		: base(maxEntriesInMemTable, metastreamMaxCount) {
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		ReOpenDb();
	}

	private void ReOpenDb() {
		Db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, WriterCheckpoint, ChaserCheckpoint));

		Db.Open();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});
		var readers = new ObjectPool<ITransactionFileReader>("Readers", 2, 5,
			() => new TFChunkReader(Db, Db.Config.WriterCheckpoint));
		var lowHasher = _logFormat.LowHasher;
		var highHasher = _logFormat.HighHasher;
		var emptyStreamId = _logFormat.EmptyStreamId;
		TableIndex = new TableIndex<TStreamId>(indexDirectory, lowHasher, highHasher, emptyStreamId,
			() => new HashListMemTable(PTableVersions.IndexV3, MaxEntriesInMemTable * 2),
			() => new TFReaderLease(readers),
			PTableVersions.IndexV3,
			int.MaxValue,
			Constants.PTableMaxReaderCountDefault,
			MaxEntriesInMemTable);
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
			new NoLRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>(),
			new NoLRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>(),
			additionalCommitChecks: true,
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
	}
}
