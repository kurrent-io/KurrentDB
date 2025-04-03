// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;

[TestFixture]
public class when_3_node_cluster_receives_second_ack_from_first_node :
	with_clustered_replication_tracking_service {
	private readonly long _firstLogPosition = 2000;
	private readonly long _secondLogPosition = 4000;
	private readonly Guid _follower1 = Guid.NewGuid();
	private readonly Guid _follower2 = Guid.NewGuid();

	protected override int ClusterSize => 3;

	public override void When() {
		BecomeLeader();
		// All of the nodes have acked the first write
		WriterCheckpoint.Write(_firstLogPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_follower1, _firstLogPosition));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_follower2, _firstLogPosition));
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent());

		ReplicatedTos.Clear();
		
		// Follower 2 has lost connection and does not ack the write
		WriterCheckpoint.Write(_secondLogPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_follower1, _secondLogPosition));
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent());
	}

	[Test]
	public void replicated_to_should_be_sent_for_the_second_position() {
		Assert.True(ReplicatedTos.TryDequeue(out var msg));
		Assert.AreEqual(_secondLogPosition, msg.LogPosition);
	}

	[Test]
	public void replication_checkpoint_should_advance() {
		Assert.AreEqual(_secondLogPosition, ReplicationCheckpoint.Read());
		Assert.AreEqual(_secondLogPosition, ReplicationCheckpoint.ReadNonFlushed());
	}
}
