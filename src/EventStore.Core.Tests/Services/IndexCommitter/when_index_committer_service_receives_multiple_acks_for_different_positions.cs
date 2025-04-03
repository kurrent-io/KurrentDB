// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.IndexCommitter;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_index_committer_service_receives_multiple_acks_for_different_positions<TLogFormat, TStreamId> : with_index_committer_service<TLogFormat, TStreamId> {

	private readonly long _logPositionP1 = 1000;
	private readonly long _logPositionP2 = 2000;
	private readonly long _logPositionP3 = 3000;
	private readonly long _logPositionCommit1 = 3100;
	private readonly long _logPositionCommit2 = 3200;
	private readonly long _logPositionCommit3 = 3300;

	public override void Given() {

		AddPendingPrepare(_logPositionP1);
		AddPendingPrepare(_logPositionP2);
		AddPendingPrepare(_logPositionP3);

		Service.Handle(new StorageMessage.CommitAck(Guid.NewGuid(), _logPositionCommit1, _logPositionP1, 0, 0));
		Service.Handle(new StorageMessage.CommitAck(Guid.NewGuid(), _logPositionCommit2, _logPositionP2, 0, 0));
		Service.Handle(new StorageMessage.CommitAck(Guid.NewGuid(), _logPositionCommit3, _logPositionP3, 0, 0));
	}

	public override void When() {
		// Reach quorum for middle commit
		ReplicationCheckpoint.Write(_logPositionCommit2 + 1);
		ReplicationCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.ReplicatedTo(_logPositionCommit2 + 1));

	}

	[Test]
	public void commit_replicated_message_should_have_been_published_for_first_two_events() {
		AssertEx.IsOrBecomesTrue(() => 2 == CommitReplicatedMgs.Count);
		Assert.True(CommitReplicatedMgs.TryDequeue(out var msg));
		Assert.AreEqual(_logPositionP1, msg.TransactionPosition);
		Assert.True(CommitReplicatedMgs.TryDequeue(out msg));
		Assert.AreEqual(_logPositionP2, msg.TransactionPosition);
	}
	[Test]
	public void index_written_message_should_have_been_published_for_first_two_events() {
		AssertEx.IsOrBecomesTrue(() => 2 == IndexWrittenMgs.Count);
		Assert.True(IndexWrittenMgs.TryDequeue(out var msg));
		Assert.AreEqual(_logPositionCommit1, msg.LogPosition);
		Assert.True(IndexWrittenMgs.TryDequeue(out msg));
		Assert.AreEqual(_logPositionCommit2, msg.LogPosition);
	}

	[Test]
	public void index_should_have_been_updated() {
		AssertEx.IsOrBecomesTrue(() => 2 == IndexCommitter.CommittedPrepares.Count);
		Assert.True(IndexCommitter.CommittedPrepares.TryDequeue(out var msg));
		Assert.AreEqual(_logPositionP1, msg.LogPosition);
		Assert.True(IndexCommitter.CommittedPrepares.TryDequeue(out msg));
		Assert.AreEqual(_logPositionP2, msg.LogPosition);
	}
}
