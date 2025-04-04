// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Replication;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.LogReplication.Tests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class data_chunk_replication<TLogFormat, TStreamId> : LogReplicationFixture<TLogFormat, TStreamId> {
	private readonly string _bulkSizedEvent = new(' ', LeaderReplicationService.BulkSize);

	private async Task<long> WriteEventsToFillChunks(
		int numChunksToFill, int numEventsPerChunk, int numEventsPerTransaction, Func<string[], Task<long>> writeEvents) {
		var eventData = new string(' ', ChunkSize / numEventsPerChunk);
		var eventsPerTransaction = Enumerable.Repeat(eventData, numEventsPerTransaction).ToArray();

		var writerPos = -1L;
		var numTransactions = numEventsPerChunk * numChunksToFill / numEventsPerTransaction;
		for (var i = 0; i < numTransactions; i++)
			writerPos = await writeEvents(eventsPerTransaction);

		if (writerPos < 0)
			throw new Exception("No events were written");

		return writerPos;
	}

	[Test]
	public async Task correctly_checkpoints_a_transaction_of_one_event() {
		await WriteEvents("stream", "event 1");

		await ConnectReplica();
		await Replicated();
		VerifyCheckpoints(1);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_a_transaction_of_two_events() {
		await WriteEvents("stream", "event 1", "event 2");

		await ConnectReplica();
		await Replicated();
		VerifyCheckpoints(1);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_two_transactions_of_two_events() {
		await WriteEvents("stream", "event 1", "event 2");
		await WriteEvents("stream", "event 1", "event 2");

		await ConnectReplica();
		await Replicated();
		VerifyCheckpoints(2);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_two_transactions_of_two_events_to_different_streams() {
		await WriteEvents("stream 1", "event 1", "event 2");
		await WriteEvents("stream 2", "event 1", "event 2");

		await ConnectReplica();
		await Replicated();
		VerifyCheckpoints(2);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_when_interrupted_at_chunk_bulk_0_then_resumed() {
		var writerPos = await WriteEvents("stream", "event 1"); // chunk bulk 0

		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: writerPos - 1, expectedFlushes: 0);
		VerifyCheckpoints(
			expectedLeaderCheckpoints: 1,
			expectedReplicaCheckpoints: 0);

		await ReconnectReplica(pauseReplication: false);
		await Replicated();
		VerifyCheckpoints(1);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_when_interrupted_at_chunk_bulk_1_then_resumed() {
		var writerPos = await WriteEvents("stream", _bulkSizedEvent); // chunk bulk 0-1

		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: writerPos - 1, expectedFlushes: 1);
		VerifyCheckpoints(
			expectedLeaderCheckpoints: 1,
			expectedReplicaCheckpoints: 0);

		await ReconnectReplica(pauseReplication: false);
		await Replicated();
		VerifyCheckpoints(1);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_when_interrupted_at_chunk_bulk_2_then_resumed() {
		await WriteEvents("stream", "event 1", "event 2"); // chunk bulk 0
		await WriteEvents("stream", "event 3"); // chunk bulk 0
		await WriteEvents("stream", _bulkSizedEvent); // chunk bulk 0-1
		await WriteEvents("stream", "event 5"); // chunk bulk 1
		await WriteEvents("stream", "event 6", "event 7"); // chunk bulk 1
		var writerPos = await WriteEvents("stream", _bulkSizedEvent); // chunk bulk 1-2

		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: writerPos - 1, expectedFlushes: 2);
		VerifyCheckpoints(
			expectedLeaderCheckpoints: 6,
			expectedReplicaCheckpoints: 5);

		await ReconnectReplica(pauseReplication: false);
		await Replicated();
		VerifyCheckpoints(6);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_when_interrupted_and_resumed_multiple_times() {
		await WriteEvents("stream", "event 1", "event 2"); // chunk bulk 0
		var writerPos0 = await WriteEvents("stream", "event 3"); // chunk bulk 0
		await WriteEvents("stream", _bulkSizedEvent); // chunk bulk 0-1
		var writerPos1 = await WriteEvents("stream", "event 5"); // chunk bulk 1
		await WriteEvents("stream", "event 6", "event 7"); // chunk bulk 1
		var writerPos2 = await WriteEvents("stream", _bulkSizedEvent); // chunk bulk 1-2
		await WriteEvents("stream", "event 9"); // chunk bulk 2

		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: writerPos0, expectedFlushes: 0);
		VerifyCheckpoints(
			expectedLeaderCheckpoints: 7,
			expectedReplicaCheckpoints: 0);

		await ReconnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: writerPos1, expectedFlushes: 1);
		VerifyCheckpoints(
			expectedLeaderCheckpoints: 7,
			expectedReplicaCheckpoints: 2);

		await ReconnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: writerPos2, expectedFlushes: 2);
		VerifyCheckpoints(
			expectedLeaderCheckpoints: 7,
			expectedReplicaCheckpoints: 5);

		await ReconnectReplica(pauseReplication: false);
		await Replicated();
		VerifyCheckpoints(7);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_when_interrupted_in_the_middle_of_a_transaction_then_resumed() {
		var eventData = new string(' ', LeaderReplicationService.BulkSize / 4);
		var events = Enumerable.Repeat(eventData, 6).ToArray();

		await WriteEvents("stream", events); // chunk bulk 0-1
		var writerPos = await WriteEvents("stream", "event 7"); // chunk bulk 1

		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: writerPos - 1, expectedFlushes: 1);
		VerifyCheckpoints(
			expectedLeaderCheckpoints: 2,
			expectedReplicaCheckpoints: 0);

		await ReconnectReplica(pauseReplication: false);
		await Replicated();
		VerifyCheckpoints(2);
		await VerifyDB(1, CancellationToken.None);
	}

	[Test]
	public async Task correctly_checkpoints_when_replicating_multiple_chunk_files() {
		const int numChunksToFill = 4;
		var writerPos = await WriteEventsToFillChunks(
			numChunksToFill: numChunksToFill,
			numEventsPerChunk: 128,
			numEventsPerTransaction: 2,
			async events => await WriteEvents("stream", events));

		Assert.AreEqual(numChunksToFill, (int)(writerPos / ChunkSize));

		const int numTransactions = 4 * 128 / 2;

		await ConnectReplica();
		await Replicated();
		VerifyCheckpoints(numChunksToFill + numTransactions);
		await VerifyDB(4 + 1, CancellationToken.None);
	}

	[Test]
	public async Task when_replica_becomes_leader_it_can_rollback_an_incomplete_log_record_spanning_one_chunk_file() {
		// calculate an empty event's size to use as a reference value to later determine the replica's writer position.
		// for log v3, this empty event size includes the stream record size. normally, the stream record size
		// should have been excluded but since we do not have a storage chaser & index committer in this test fixture,
		// the stream record will be written again when writing events directly to the replica, so this size is appropriate.
		var emptyEventSize = await WriteEvents("s", string.Empty); // chunk bulk 0

		await WriteEvents("stream", "event 1", "event 2"); // chunk bulk 0
		var leaderWriterPos = await WriteEvents("stream", _bulkSizedEvent); // chunk bulk 0-1 (the log record that'll be "half replicated")
		Assert.AreEqual(0, leaderWriterPos / ChunkSize);

		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: leaderWriterPos - 1, expectedFlushes: 1);
		await ReplicaBecomesLeader();

		// verify if the incomplete log record was properly rolled back by checking if the
		// replica's writer position is at the writer checkpoint's position.
		var replicaWriterChk = ReplicaWriterCheckpoint;
		Assert.AreEqual(0, replicaWriterChk / ChunkSize);
		var replicaWriterPos = await WriteEventsToReplica("s", string.Empty);
		Assert.AreEqual(replicaWriterChk, replicaWriterPos - emptyEventSize);

		// check if we can create new chunks without any issue
		replicaWriterPos = await WriteEventsToFillChunks(
			numChunksToFill: 2,
			numEventsPerChunk: 128,
			numEventsPerTransaction: 2,
			async events => await WriteEventsToReplica("stream", events));

		Assert.AreEqual(2, replicaWriterPos / ChunkSize);
		Assert.AreEqual(ReplicaWriterCheckpoint, replicaWriterPos);
	}

	[Test]
	public async Task when_replica_becomes_leader_it_can_rollback_an_incomplete_transaction_spanning_one_chunk_file() {
		// see above notes regarding why we calculate an empty event's size
		var emptyEventSize = await WriteEvents("s", string.Empty); // chunk bulk 0

		var eventData = new string(' ', LeaderReplicationService.BulkSize / 4);
		var events = Enumerable.Repeat(eventData, 6).ToArray();

		var leaderWriterPos = await WriteEvents("stream", events); // chunk bulk 0-1 (the transaction that'll be "half replicated")
		Assert.AreEqual(0, leaderWriterPos / ChunkSize);

		await ConnectReplica(pauseReplication: true);
		await ResumeReplicationUntil(maxLogPosition: leaderWriterPos - 1, expectedFlushes: 1);
		await ReplicaBecomesLeader();

		// verify if the incomplete transaction was properly rolled back by checking if the
		// replica's writer position is at the writer checkpoint's position.
		var replicaWriterChk = ReplicaWriterCheckpoint;
		Assert.AreEqual(0, replicaWriterChk / ChunkSize);

		var replicaWriterPos = await WriteEventsToReplica("s", string.Empty);
		Assert.AreEqual(replicaWriterChk, replicaWriterPos - emptyEventSize);

		// check if we can create new chunks without any issue
		replicaWriterPos = await WriteEventsToFillChunks(
			numChunksToFill: 2,
			numEventsPerChunk: 128,
			numEventsPerTransaction: 2,
			async evts => await WriteEventsToReplica("stream", evts));

		Assert.AreEqual(2, replicaWriterPos / ChunkSize);
		Assert.AreEqual(ReplicaWriterCheckpoint, replicaWriterPos);
	}

	// note: we do not have tests for incomplete transactions that span more than one chunk file as this scenario is no longer possible.
}
