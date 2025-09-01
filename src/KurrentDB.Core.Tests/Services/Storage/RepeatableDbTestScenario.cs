// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Index;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.Tests.TransactionLog;
using KurrentDB.Core.Tests.TransactionLog.Scavenging.Helpers;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.Util;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage;

[TestFixture]
public abstract class RepeatableDbTestScenario<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected readonly int MaxEntriesInMemTable;
	protected TableIndex<TStreamId> TableIndex;
	protected IReadIndex<TStreamId> ReadIndex;
	protected LogFormatAbstractor<TStreamId> _logFormat;

	protected DbResult DbRes;
	protected TFChunkDbCreationHelper<TLogFormat, TStreamId> DbCreationHelper;

	private readonly int _metastreamMaxCount;

	protected RepeatableDbTestScenario(int maxEntriesInMemTable = 20, int metastreamMaxCount = 1) {
		Ensure.Positive(maxEntriesInMemTable, "maxEntriesInMemTable");
		MaxEntriesInMemTable = maxEntriesInMemTable;
		_metastreamMaxCount = metastreamMaxCount;
	}

	public async ValueTask CreateDb(Rec[] records, CancellationToken token = default) {
		if (DbRes is not null) {
			await DbRes.Db.DisposeAsync();
		}

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		var dbConfig = TFChunkHelper.CreateSizedDbConfig(PathName, 0, chunkSize: 1024 * 1024);
		var dbHelper = await TFChunkDbCreationHelper<TLogFormat, TStreamId>.CreateAsync(dbConfig, _logFormat, token: token);

		DbRes = await dbHelper.Chunk(records).CreateDb(token: token);

		DbRes.Db.Config.WriterCheckpoint.Flush();
		DbRes.Db.Config.ChaserCheckpoint.Write(DbRes.Db.Config.WriterCheckpoint.Read());
		DbRes.Db.Config.ChaserCheckpoint.Flush();

		var reader = new TFChunkReader(DbRes.Db, DbRes.Db.Config.WriterCheckpoint);

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
