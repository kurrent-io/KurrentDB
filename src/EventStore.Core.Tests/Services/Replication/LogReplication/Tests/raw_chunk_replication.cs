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
public class raw_chunk_replication<TLogFormat, TStreamId> : LogReplicationWithExistingDbFixture<TLogFormat, TStreamId> {
	private const int NumCheckpoints = 1 + /* chunk 0-0 (raw): 1 chunk completion */
	                                   1 + /* chunk 1-2 (raw): 1 chunk completion */
	                                   4 + /* chunk 3-3 (non-raw): 3 complete transactions, 1 chunk completion */
	                                   6 + /* chunk 4-4 (non-raw): 4 complete transactions, 1 incomplete
	                                          transaction at end (checkpointed for backwards compatibility), 1 chunk completion */
	                                   1 + /* chunk 5-5 (raw): 1 chunk completion */
	                                   1 + /* chunk 6-9 (raw): 1 chunk completion */
	                                   2   /* chunk 10-10 (non-raw): 2 complete transactions */;

	private const int NumLogicalChunks = 11;

	protected override async Task CreateChunks(TFChunkDb db) {
		LogFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = Path.Combine(db.Config.Path, "index")
		});

		var recs0 = GenerateLogRecords(0, new[] { 1, -3, 2, 3 }, out _);
		var recs1 = GenerateLogRecords(1, new[] { 2, 2, 1, 2, 7, -3 }, out _);
		var recs3 = GenerateLogRecords(3, new[] { 3, -3, 3, 3 }, out _);
		var recs4 = GenerateLogRecords(4, new[] { 4, 3, 2, 1, -2 }, out _);
		var recs5 = GenerateLogRecords(5, new[] { -5 }, out _);
		var recs6 = GenerateLogRecords(6, new[] { 1, 1, 1, 1, -1 }, out _);
		var recs10 = GenerateLogRecords(10, new[] { 1, 2 }, out _);

		await CreateChunk(db, raw: true,  complete: true,   0,  0, recs0);
		await CreateChunk(db, raw: true,  complete: true,   1,  2, recs1);
		await CreateChunk(db, raw: false, complete: true,   3,  3, recs3);
		await CreateChunk(db, raw: false, complete: true,   4,  4, recs4);
		await CreateChunk(db, raw: true,  complete: true,   5,  5, recs5);
		await CreateChunk(db, raw: true,  complete: true,   6,  9, recs6);
		await CreateChunk(db, raw: false, complete: false, 10, 10, recs10);

		db.Config.WriterCheckpoint.Flush();
	}


	[Test]
	public async Task can_replicate() {
		await ConnectReplica();
		await Replicated();

		VerifyCheckpoints(NumCheckpoints);
		await VerifyDB(NumLogicalChunks, CancellationToken.None);
	}

	[Test]
	public async Task can_replicate_when_interrupted_in_raw_chunk_then_resumed() {
		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(
			rawChunkStartNumber: 1,
			rawChunkEndNumber: 2,
			maxRawPosition: TFConsts.ChunkHeaderSize + DataSize * 3,
			expectedFlushes: 1);

		VerifyCheckpoints(
			expectedLeaderCheckpoints: NumCheckpoints,
			expectedReplicaCheckpoints: 1);

		await ReconnectReplica();
		await Replicated();

		VerifyCheckpoints(NumCheckpoints);
		await VerifyDB(NumLogicalChunks, CancellationToken.None);
	}

	[Test]
	public async Task can_replicate_when_interrupted_at_boundary_of_raw_chunk_with_complete_transactions_then_resumed() {
		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(
			rawChunkStartNumber: 1,
			rawChunkEndNumber: 2,
			maxRawPosition: 0,
			expectedFlushes: 1);

		VerifyCheckpoints(
			expectedLeaderCheckpoints: NumCheckpoints,
			expectedReplicaCheckpoints: 1);

		await ReconnectReplica();
		await Replicated();

		VerifyCheckpoints(NumCheckpoints);
		await VerifyDB(NumLogicalChunks, CancellationToken.None);
	}

	[Test]
	public async Task can_replicate_when_interrupted_at_boundary_of_raw_chunk_with_incomplete_transactions_then_resumed() {
		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: 3 * ChunkSize, expectedFlushes: 2);

		VerifyCheckpoints(
			expectedLeaderCheckpoints: NumCheckpoints,
			expectedReplicaCheckpoints: 2);

		await ReconnectReplica();
		await Replicated();

		VerifyCheckpoints(NumCheckpoints);
		await VerifyDB(NumLogicalChunks, CancellationToken.None);
	}

	[Test]
	public async Task can_replicate_when_interrupted_at_boundary_of_raw_chunk_with_no_transactions_then_resumed() {
		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(
			rawChunkStartNumber: 6,
			rawChunkEndNumber: 9,
			maxRawPosition: 0,
			expectedFlushes: 15);

		VerifyCheckpoints(
			expectedLeaderCheckpoints: NumCheckpoints,
			expectedReplicaCheckpoints: 13);

		await ReconnectReplica();
		await Replicated();

		VerifyCheckpoints(NumCheckpoints);
		await VerifyDB(NumLogicalChunks, CancellationToken.None);
	}

	[Test]
	public async Task can_replicate_when_interrupted_in_data_chunk_then_resumed() {
		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: 3 * ChunkSize + 5 * DataSize, expectedFlushes: 4);

		await ReconnectReplica();
		await Replicated();

		VerifyCheckpoints(NumCheckpoints);
		await VerifyDB(NumLogicalChunks, CancellationToken.None);
	}
}
