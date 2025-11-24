// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Index;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Core.Util;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage.Transactions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_rebuilding_index_for_partially_persisted_transaction<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	public when_rebuilding_index_for_partially_persisted_transaction() : base(maxEntriesInMemTable: 10) {
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		ReadIndex.Close();
		ReadIndex.Dispose();
		TableIndex.Close(removeFiles: false);

		var reader = new TFChunkReader(Db, WriterCheckpoint);
		var lowHasher = _logFormat.LowHasher;
		var highHasher = _logFormat.HighHasher;
		var emptyStreamId = _logFormat.EmptyStreamId;
		TableIndex = new TableIndex<TStreamId>(GetFilePathFor("index"), lowHasher, highHasher, emptyStreamId,
			() => new HashListMemTable(PTableVersions.IndexV2, maxSize: MaxEntriesInMemTable * 2),
			reader,
			PTableVersions.IndexV2,
			5,
			MaxEntriesInMemTable);
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
			metastreamMaxCount: 1,
			hashCollisionReadLimit: Opts.HashCollisionReadLimitDefault,
			skipIndexScanOnReads: Opts.SkipIndexScanOnReadsDefault,
			replicationCheckpoint: Db.Config.ReplicationCheckpoint,
			indexCheckpoint: Db.Config.IndexCheckpoint,
			indexStatusTracker: new IndexStatusTracker.NoOp(),
			indexTracker: new IndexTracker.NoOp(),
			cacheTracker: new CacheHitsMissesTracker.NoOp());
		await readIndex.IndexCommitter.Init(ChaserCheckpoint.Read(), CancellationToken.None);
		ReadIndex = readIndex;
	}

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var begin = await WriteTransactionBegin("ES", ExpectedVersion.Any, token);
		for (int i = 0; i < 15; ++i) {
			await WriteTransactionEvent(Guid.NewGuid(), begin.LogPosition, i, "ES", i, "data" + i, PrepareFlags.Data, token: token);
		}

		await WriteTransactionEnd(Guid.NewGuid(), begin.LogPosition, "ES", token);
		await WriteCommit(Guid.NewGuid(), begin.LogPosition, "ES", 0, token);
	}

	[Test]
	public async Task sequence_numbers_are_not_broken() {
		for (int i = 0; i < 15; ++i) {
			var result = await ReadIndex.ReadEvent("ES", i, CancellationToken.None);
			Assert.AreEqual(ReadEventResult.Success, result.Result);
			Assert.AreEqual(Helper.UTF8NoBom.GetBytes("data" + i), result.Record.Data.ToArray());
		}
	}
}
