// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.Util;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage;

[TestFixture]
public abstract class SimpleDbTestScenario<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected readonly int MaxEntriesInMemTable;
	protected LogFormatAbstractor<TStreamId> _logFormat;
	protected TableIndex<TStreamId> TableIndex;
	protected IReadIndex<TStreamId> ReadIndex;

	protected DbResult DbRes;

	protected abstract ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token);

	private readonly long _metastreamMaxCount;

	protected SimpleDbTestScenario(int maxEntriesInMemTable = 20, long metastreamMaxCount = 1) {
		Ensure.Positive(maxEntriesInMemTable, "maxEntriesInMemTable");
		MaxEntriesInMemTable = maxEntriesInMemTable;
		_metastreamMaxCount = metastreamMaxCount;
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		var dbConfig = TFChunkHelper.CreateSizedDbConfig(PathName, 0, chunkSize: 1024 * 1024);
		var dbCreationHelper = await TFChunkDbCreationHelper<TLogFormat, TStreamId>.CreateAsync(dbConfig, _logFormat);

		DbRes = await CreateDb(dbCreationHelper, CancellationToken.None);

		DbRes.Db.Config.WriterCheckpoint.Flush();
		DbRes.Db.Config.ChaserCheckpoint.Write(DbRes.Db.Config.WriterCheckpoint.Read());
		DbRes.Db.Config.ChaserCheckpoint.Flush();

		var readers = new ObjectPool<ITransactionFileReader>(
			"Readers", 2, 2, () => new TFChunkReader(DbRes.Db, DbRes.Db.Config.WriterCheckpoint));

		var lowHasher = _logFormat.LowHasher;
		var highHasher = _logFormat.HighHasher;
		var emptyStreamId = _logFormat.EmptyStreamId;
		TableIndex = new TableIndex<TStreamId>(indexDirectory, lowHasher, highHasher, emptyStreamId,
			() => new HashListMemTable(PTableVersions.IndexV2, MaxEntriesInMemTable * 2),
			() => new TFReaderLease(readers),
			PTableVersions.IndexV2,
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
			metastreamMaxCount: _metastreamMaxCount,
			hashCollisionReadLimit: Opts.HashCollisionReadLimitDefault,
			skipIndexScanOnReads: Opts.SkipIndexScanOnReadsDefault,
			replicationCheckpoint: DbRes.Db.Config.ReplicationCheckpoint,
			indexCheckpoint: DbRes.Db.Config.IndexCheckpoint,
			indexStatusTracker: new IndexStatusTracker.NoOp(),
			indexTracker: new IndexTracker.NoOp(),
			cacheTracker: new CacheHitsMissesTracker.NoOp());

		await readIndex.IndexCommitter.Init(DbRes.Db.Config.ChaserCheckpoint.Read(), CancellationToken.None);
		ReadIndex = readIndex;
	}

	public override async Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		await DbRes.Db.DisposeAsync();

		await base.TestFixtureTearDown();
	}
}
