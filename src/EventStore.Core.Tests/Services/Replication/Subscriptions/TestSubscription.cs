using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Replication.ReadStream {
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
}
