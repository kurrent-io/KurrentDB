// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.LogReplication.Tests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class data_chunk_replication_with_existing_db<TLogFormat, TStreamId> : LogReplicationWithExistingDbFixture<TLogFormat, TStreamId> {
	private const int NumCheckpoints = 5 + /* chunk 0-0 (non-raw): 3 complete transactions, 1 incomplete transaction
	                                          at end (checkpointed for backwards compatibility), 1 chunk completion */
	                                   0   /* chunk 1-1 (non-raw): 0 complete transactions */;

	private const int NumLogicalChunks = 2;

	protected override async Task CreateChunks(TFChunkDb db) {
		LogFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = Path.Combine(db.Config.Path, "index")
		});

		var recs0 = GenerateLogRecords(0, new[] { 1, -3, 2, -1, 3, -5 }, out _);
		var recs1 = GenerateLogRecords(1, Array.Empty<int>(), out _);

		await CreateChunk(db, raw: false, complete: true,  0, 0, recs0);
		await CreateChunk(db, raw: false, complete: false, 1, 1, recs1);

		db.Config.WriterCheckpoint.Flush();
	}


	[Test]
	public async Task can_replicate() {
		await ConnectReplica();
		await Replicated();

		VerifyCheckpoints(NumCheckpoints);
		await VerifyDB(NumLogicalChunks, CancellationToken.None);
	}
}
