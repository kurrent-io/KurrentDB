// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.IndexCommitter;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_index_committer_service_receives_multiple_acks_for_different_positions_out_of_order<TLogFormat, TStreamId>
	: with_index_committer_service<TLogFormat, TStreamId> {

	private readonly Guid _correlationId1 = Guid.NewGuid();
	private readonly Guid _correlationId2 = Guid.NewGuid();

	private readonly long _logPosition1 = 1000;
	private readonly long _logPosition2 = 2000;
	private readonly long _logPosition3 = 3000;
	private readonly long _logPosition4 = 4000;

	public override void Given() { }

	public override void When() {


		AddPendingPrepare(_logPosition2);
		AddPendingPrepare(_logPosition1);
		Service.Handle(new StorageMessage.CommitAck(_correlationId2, _logPosition4, _logPosition2, 0, 0));
		Service.Handle(new StorageMessage.CommitAck(_correlationId1, _logPosition3, _logPosition1, 0, 0));

			
		ReplicationCheckpoint.Write(_logPosition4 + 1);
		ReplicationCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.ReplicatedTo(_logPosition4 + 1));
	}
	
	[Test]
	public void commit_replicated_message_should_have_been_published_for_both_events() {
		AssertEx.IsOrBecomesTrue(() => 2 == CommitReplicatedMgs.Count);
		Assert.True(CommitReplicatedMgs.TryDequeue(out var msg));
		Assert.AreEqual(_correlationId1, msg.CorrelationId);
		Assert.True(CommitReplicatedMgs.TryDequeue(out msg));
		Assert.AreEqual(_correlationId2, msg.CorrelationId);
	}
	[Test]
	public void index_written_message_should_have_been_published_for_both_events() {
		AssertEx.IsOrBecomesTrue(() => 2 == IndexWrittenMgs.Count);
		Assert.True(IndexWrittenMgs.TryDequeue(out var msg));
		Assert.AreEqual(_logPosition3, msg.LogPosition);
		Assert.True(IndexWrittenMgs.TryDequeue(out msg));
		Assert.AreEqual(_logPosition4, msg.LogPosition);
	}
	[Test]
	public void prepare_ack_message_should_have_been_published_for_both_events() {
		AssertEx.IsOrBecomesTrue(() => 2 == IndexCommitter.CommittedPrepares.Count);
		Assert.True(IndexCommitter.CommittedPrepares.TryDequeue(out var msg));
		Assert.AreEqual(_logPosition1, msg.LogPosition);
		Assert.True(IndexCommitter.CommittedPrepares.TryDequeue(out msg));
		Assert.AreEqual(_logPosition2, msg.LogPosition);
	}
}
