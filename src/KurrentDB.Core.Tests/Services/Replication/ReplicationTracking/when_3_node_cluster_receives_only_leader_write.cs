// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messages;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Replication.ReplicationTracking;

[TestFixture]
public class when_3_node_cluster_receives_only_leader_write : with_clustered_replication_tracking_service {
	private long _logPosition = 4000;

	protected override int ClusterSize => 3;

	public override void When() {
		BecomeLeader();
		WriterCheckpoint.Write(_logPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
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
