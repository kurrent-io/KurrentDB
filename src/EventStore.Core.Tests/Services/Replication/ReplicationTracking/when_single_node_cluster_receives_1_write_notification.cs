// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;

[TestFixture]
public class when_single_node_cluster_receives_1_write_notification : with_clustered_replication_tracking_service {
	private long _logPosition = 4000;
	protected override int ClusterSize => 1;

	public override void When() {
		BecomeLeader();
		WriterCheckpoint.Write(_logPosition);
		WriterCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		AssertEx.IsOrBecomesTrue(()=> Service.IsCurrent());
	}

	[Test]
	public void replicated_to_should_be_sent() {
		Assert.AreEqual(1, ReplicatedTos.Count);
		Assert.True(ReplicatedTos.TryDequeue(out var msg));
		Assert.AreEqual(_logPosition, msg.LogPosition);
	}
	[Test]
	public void replication_checkpoint_should_advance() {
		Assert.AreEqual(_logPosition, ReplicationCheckpoint.Read());		
		Assert.AreEqual(_logPosition, ReplicationCheckpoint.ReadNonFlushed());		
	}	
}
