// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Replication.Subscriptions;

public class TestSubscription<TLogFormat, TStreamId> {
	public MiniClusterNode<TLogFormat, TStreamId> Node;
	public CountdownEvent SubscriptionsConfirmed;
	public CountdownEvent EventAppeared;
	public List<ClientMessage.StreamEventAppeared> StreamEvents;
	public string StreamId;

	public TestSubscription(MiniClusterNode<TLogFormat, TStreamId> node, int expectedEvents, string streamId,
		CountdownEvent subscriptionsConfirmed) {
		Node = node;
		SubscriptionsConfirmed = subscriptionsConfirmed;
		EventAppeared = new CountdownEvent(expectedEvents);
		StreamId = streamId;
	}

	public void CreateSubscription() {
		var subscribeMsg = new ClientMessage.SubscribeToStream(Guid.NewGuid(), Guid.NewGuid(),
			new CallbackEnvelope(x => {
				switch (x.GetType().Name) {
					case "SubscriptionConfirmation":
						SubscriptionsConfirmed.Signal();
						break;
					case "StreamEventAppeared":
						EventAppeared.Signal();
						break;
					case "SubscriptionDropped":
						break;
					default:
						Assert.Fail("Unexpected message type :" + x.GetType().Name);
						break;
				}
			}), Guid.NewGuid(), StreamId, false, SystemAccounts.System);
		Node.Node.MainQueue.Publish(subscribeMsg);
	}
}
