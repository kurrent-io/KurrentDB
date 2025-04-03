// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.Service;

[TestFixture]
public class when_writing_and_deposed_as_leader_and_replica_moves_forward : RequestManagerServiceSpecification {
	
	protected override void Given() {
		Dispatcher.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		Dispatcher.Publish(new ClientMessage.WriteEvents(InternalCorrId, ClientCorrId, Envelope, true, StreamId, ExpectedVersion.Any, new[] { DummyEvent() }, null));
		Dispatcher.Publish(new SystemMessage.BecomePreReplica(Guid.NewGuid(), Guid.NewGuid(), FakeMemberInfo()));
	}

	protected override Message When() {
		return new ReplicationTrackingMessage.IndexedTo(LogPosition);
	}

	[Test]
	public void the_old_write_is_not_acknowledged() {
		Assert.AreEqual(0, Envelope.Replies.Count);
	}

	private static MemberInfo FakeMemberInfo() {
		var ipAddress = "127.0.0.1";
		var port = 1113;
		return EventStore.Core.Cluster.MemberInfo.Initial(Guid.Empty, DateTime.UtcNow,
			VNodeState.Unknown, true,
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			null, 0, 0, 0, false);
	}
}
