// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;

[TestFixture]
public class when_cluster_receives_multiple_stale_acks :
	with_clustered_replication_tracking_service {
	private readonly long _firstLogPosition = 2000;
	private readonly Guid _replica1 = Guid.NewGuid();
	private readonly Guid _replica2 = Guid.NewGuid();

	protected override int ClusterSize => 3;

	public override void When() {
		BecomeLeader();
		// All of the nodes have acked the first write
		WriterCheckpoint.Write(_firstLogPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replica1, _firstLogPosition));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replica2, _firstLogPosition - 100));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(Guid.NewGuid(), _firstLogPosition - 100));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(Guid.NewGuid(), _firstLogPosition - 100));
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent());
	}

	[Test]
	public void replicated_to_should_be_sent_for_the_first_position() {
		Assert.True(ReplicatedTos.TryDequeue(out var msg));
		Assert.AreEqual(_firstLogPosition, msg.LogPosition);
	}

	[Test]
	public void replication_checkpoint_be_at_the_first_position() {
		Assert.AreEqual(_firstLogPosition, ReplicationCheckpoint.Read());
		Assert.AreEqual(_firstLogPosition, ReplicationCheckpoint.ReadNonFlushed());
	}
}
