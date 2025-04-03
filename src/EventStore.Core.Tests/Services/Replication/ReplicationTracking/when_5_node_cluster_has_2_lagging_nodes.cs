// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;


[TestFixture]
public class when_5_node_cluster_has_2_lagging_nodes : with_clustered_replication_tracking_service {
	private readonly long _firstLogPosition = 2000;
	private readonly long _secondLogPosition = 4000;
	private Guid[] _followers;

	protected override int ClusterSize => 5;
	
	public override void When() {
		_followers = new [] {Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()};
		BecomeLeader();
		// All of the nodes have acked the first write
		WriterCheckpoint.Write(_firstLogPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		foreach (var follower in _followers) {
			Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(follower, _firstLogPosition));
		}
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent());

		ReplicatedTos.Clear();
		
		// Followers 3 and 4 are lagging behind, they ack the previous positions
		WriterCheckpoint.Write(_secondLogPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_followers[0], _secondLogPosition));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_followers[1], _secondLogPosition));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_followers[2], _firstLogPosition));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_followers[3], _firstLogPosition));
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
