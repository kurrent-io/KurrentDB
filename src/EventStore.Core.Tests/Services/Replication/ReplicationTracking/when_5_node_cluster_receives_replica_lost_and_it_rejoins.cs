// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;

[TestFixture]
public class when_5_node_cluster_receives_replica_lost_and_it_rejoins : with_clustered_replication_tracking_service {
	//n.b. the replica may get a new id, but we shouldn't fail if it doesn't

	private readonly long _logPosition = 4000;
	private readonly long _logPosition2 = 5000;
	private readonly Guid _replicaId1 = Guid.NewGuid();
	private readonly Guid _replicaId2 = Guid.NewGuid();

	protected override int ClusterSize => 5;
	
	public override void When() {
		BecomeLeader();
		WriterCheckpoint.Write(_logPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replicaId1, _logPosition));
		Service.Handle(new SystemMessage.VNodeConnectionLost(
			PortsHelper.GetLoopback(),
			Guid.NewGuid(),
			_replicaId1));
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent()); //n.b. we still don't have quorum here

		//moving directly to log position 2 after rejoin
		WriterCheckpoint.Write(_logPosition2);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replicaId1, _logPosition2));
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replicaId2, _logPosition2));
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
