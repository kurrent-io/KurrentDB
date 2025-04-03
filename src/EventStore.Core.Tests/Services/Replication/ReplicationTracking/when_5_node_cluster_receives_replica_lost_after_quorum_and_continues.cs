// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;

[TestFixture]
public class when_5_node_cluster_receives_replica_lost_after_quorum_and_continues : with_clustered_replication_tracking_service {
	private readonly long _logPosition = 4000;
	private readonly long _logPosition2 = 5000;
	private readonly Guid _replica1 = Guid.NewGuid();
	private readonly Guid _replica2 = Guid.NewGuid();
	private readonly Guid _replica3 = Guid.NewGuid();

	
	protected override int ClusterSize => 5;

	public override void When() {
		BecomeLeader();
		WriterCheckpoint.Write(_logPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replica1, _logPosition));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replica2, _logPosition));
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent());

		Service.Handle(new SystemMessage.VNodeConnectionLost(
				PortsHelper.GetLoopback(),
				Guid.NewGuid(),
							_replica1));

		ReplicatedTos.Clear();
		WriterCheckpoint.Write(_logPosition2);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replica2, _logPosition2));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replica3, _logPosition2));
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent());
	}

	[Test]
	public void replicated_to_should_be_sent() {
		AssertEx.IsOrBecomesTrue(() => 1 == ReplicatedTos.Count);
		Assert.True(ReplicatedTos.TryDequeue(out var msg));
		Assert.AreEqual(_logPosition2, msg.LogPosition);
	}
	[Test]
	public void replication_checkpoint_should_advance() {
		Assert.AreEqual(_logPosition2, ReplicationCheckpoint.Read());
		Assert.AreEqual(_logPosition2, ReplicationCheckpoint.ReadNonFlushed());
	}
}
