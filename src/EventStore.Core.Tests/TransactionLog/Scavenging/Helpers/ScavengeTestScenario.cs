// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Caching;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using EventStore.Core.Util;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Metrics;

namespace EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;

[TestFixture]
public abstract class ScavengeTestScenario<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected IReadIndex<TStreamId> ReadIndex;

	protected TFChunkDb Db {
		get { return _dbResult.Db; }
	}

	private readonly int _metastreamMaxCount;
	private DbResult _dbResult;
	private ILogRecord[][] _keptRecords;
	private bool _checked;
	private LogFormatAbstractor<TStreamId> _logFormat;

	protected virtual bool UnsafeIgnoreHardDelete() {
		return false;
	}

	protected ScavengeTestScenario(int metastreamMaxCount = 1) {
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
		_dbResult = await CreateDb(dbCreationHelper, CancellationToken.None);
		_keptRecords = KeptRecords(_dbResult);

		_dbResult.Db.Config.WriterCheckpoint.Flush();
		_dbResult.Db.Config.ChaserCheckpoint.Write(_dbResult.Db.Config.WriterCheckpoint.Read());
		_dbResult.Db.Config.ChaserCheckpoint.Flush();

		var readerPool = new ObjectPool<ITransactionFileReader>(
			"ReadIndex readers pool", Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
			() => new TFChunkReader(_dbResult.Db, _dbResult.Db.Config.WriterCheckpoint));
		var lowHasher = _logFormat.LowHasher;
		var highHasher = _logFormat.HighHasher;
		var emptyStreamId = _logFormat.EmptyStreamId;
		var tableIndex = new TableIndex<TStreamId>(indexDirectory, lowHasher, highHasher, emptyStreamId,
			() => new HashListMemTable(PTableVersions.IndexV3, maxSize: 200),
			() => new TFReaderLease(readerPool),
			PTableVersions.IndexV3,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: 100,
			maxTablesPerLevel: 2);
		_logFormat.StreamNamesProvider.SetTableIndex(tableIndex);
		var readIndex = new ReadIndex<TStreamId>(new NoopPublisher(), readerPool, tableIndex,
			_logFormat.StreamNameIndexConfirmer,
			_logFormat.StreamIds,
			_logFormat.StreamNamesProvider,
			_logFormat.EmptyStreamId,
			_logFormat.StreamIdValidator,
			_logFormat.StreamIdSizer,
			_logFormat.StreamExistenceFilter,
			_logFormat.StreamExistenceFilterReader,
			_logFormat.EventTypeIndexConfirmer,
			new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>("LastEventNumber", 100),
			new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>("StreamMetadata", 100),
			true, _metastreamMaxCount,
			Opts.HashCollisionReadLimitDefault, Opts.SkipIndexScanOnReadsDefault,
			_dbResult.Db.Config.ReplicationCheckpoint,_dbResult.Db.Config.IndexCheckpoint,
			new IndexStatusTracker.NoOp(),
			new IndexTracker.NoOp(),
			new CacheHitsMissesTracker.NoOp());
		await readIndex.IndexCommitter.Init(_dbResult.Db.Config.WriterCheckpoint.Read(), CancellationToken.None);
		ReadIndex = readIndex;

		var scavenger = new TFChunkScavenger<TStreamId>(Serilog.Log.Logger, _dbResult.Db, new FakeTFScavengerLog(), tableIndex, ReadIndex,
			_logFormat.Metastreams,
			unsafeIgnoreHardDeletes: UnsafeIgnoreHardDelete());
		await scavenger.Scavenge(alwaysKeepScavenged: true, mergeChunks: false);
	}

	public override async Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		ReadIndex.Close();
		await _dbResult.Db.DisposeAsync();

		await base.TestFixtureTearDown();

		if (!_checked)
			throw new Exception("Records were not checked. Probably you forgot to call CheckRecords() method.");
	}

	protected abstract ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token);

	protected abstract ILogRecord[][] KeptRecords(DbResult dbResult);

	protected async Task CheckRecords(CancellationToken token = default) {
		_checked = true;
		Assert.AreEqual(_keptRecords.Length, _dbResult.Db.Manager.ChunksCount, "Wrong chunks count.");

		for (int i = 0; i < _keptRecords.Length; ++i) {
			var chunk = await _dbResult.Db.Manager.GetInitializedChunk(i, token);

			var chunkRecords = new List<ILogRecord>();
			RecordReadResult result = await chunk.TryReadFirst(token);
			while (result.Success) {
				chunkRecords.Add(result.LogRecord);
				result = await chunk.TryReadClosestForward((int)result.NextPosition, CancellationToken.None);
			}

			Assert.AreEqual(_keptRecords[i].Length, chunkRecords.Count, "Wrong number of records in chunk #{0}", i);

			for (int j = 0; j < _keptRecords[i].Length; ++j) {
				Assert.AreEqual(_keptRecords[i][j], chunkRecords[j], "Wrong log record #{0} read from chunk #{1}",
					j, i);
			}
		}
	}
}
