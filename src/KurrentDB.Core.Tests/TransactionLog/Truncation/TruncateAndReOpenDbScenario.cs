// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Index;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.Util;

namespace KurrentDB.Core.Tests.TransactionLog.Truncation;

public abstract class TruncateAndReOpenDbScenario<TLogFormat, TStreamId> : TruncateScenario<TLogFormat, TStreamId> {
	protected TruncateAndReOpenDbScenario(int maxEntriesInMemTable = 100, int metastreamMaxCount = 1)
		: base(maxEntriesInMemTable, metastreamMaxCount) {
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		await ReOpenDb(CancellationToken.None);
	}

	private async ValueTask ReOpenDb(CancellationToken token) {
		Db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, WriterCheckpoint, ChaserCheckpoint));

		await Db.Open(token: token);

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});
		var reader = new TFChunkReader(Db, Db.Config.WriterCheckpoint);
		var lowHasher = _logFormat.LowHasher;
		var highHasher = _logFormat.HighHasher;
		var emptyStreamId = _logFormat.EmptyStreamId;
		TableIndex = new TableIndex<TStreamId>(indexDirectory, lowHasher, highHasher, emptyStreamId,
			() => new HashListMemTable(PTableVersions.IndexV3, MaxEntriesInMemTable * 2),
			reader,
			PTableVersions.IndexV3,
			int.MaxValue,
			Constants.PTableMaxReaderCountDefault,
			MaxEntriesInMemTable);
		_logFormat.StreamNamesProvider.SetTableIndex(TableIndex);
		var readIndex = new ReadIndex<TStreamId>(new NoopPublisher(),
			reader,
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
		await readIndex.IndexCommitter.Init(ChaserCheckpoint.Read(), token);
		ReadIndex = readIndex;
	}
}
