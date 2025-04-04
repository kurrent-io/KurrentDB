// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;

[TestFixture]
public class when_5_node_cluster_receives_replica_lost_before_quorum : with_clustered_replication_tracking_service {
	private readonly long _logPosition = 4000;
	private readonly Guid _replicaId1 = Guid.NewGuid();
	private readonly Guid _replicaId2 = Guid.NewGuid();
	private readonly Guid _replicaId3 = Guid.NewGuid();


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
		Service.Handle(new ReplicationTrackingMessage.ReplicaWriteAck(_replicaId2, _logPosition));
		AssertEx.IsOrBecomesTrue(() => Service.IsCurrent());
	}

	[Test]
	public void replicated_to_should_not_be_sent() {
		Assert.AreEqual(0, ReplicatedTos.Count);
	}

	[Test]
	public void replication_checkpoint_should_not_advance() {
		Assert.AreEqual(0, ReplicationCheckpoint.Read());
		Assert.AreEqual(0, ReplicationCheckpoint.ReadNonFlushed());
	}
}
