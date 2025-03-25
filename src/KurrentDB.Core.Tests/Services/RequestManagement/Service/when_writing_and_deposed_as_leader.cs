// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.Service;

[TestFixture]
public class when_writing_and_deposed_as_leader : RequestManagerServiceSpecification {

	protected override void Given() {
		Dispatcher.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		Dispatcher.Publish(ClientMessage.WriteEvents.ForSingleStream(InternalCorrId, ClientCorrId, Envelope, true, StreamId, ExpectedVersion.Any, new[] { DummyEvent() }, null));
	}

	protected override Message When() {
		return new SystemMessage.BecomePreReplica(Guid.NewGuid(), Guid.NewGuid(), FakeMemberInfo());
	}

	[Test]
	public void the_envelope_is_replied_to_with_commit_timeout() {
		Assert.AreEqual(1, Envelope.Replies.Count);
		Assert.IsInstanceOf<ClientMessage.WriteEventsCompleted>(Envelope.Replies[0]);
		var response = (ClientMessage.WriteEventsCompleted)Envelope.Replies[0];
		Assert.AreEqual(OperationResult.CommitTimeout, response.Result);
		Assert.AreEqual("Request canceled by server", response.Message);
	}

	private static MemberInfo FakeMemberInfo() {
		var ipAddress = "127.0.0.1";
		var port = 1113;
		return MemberInfo.Initial(Guid.Empty, DateTime.UtcNow,
			VNodeState.Unknown, true,
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			null, 0, 0, 0, false);
	}
}
