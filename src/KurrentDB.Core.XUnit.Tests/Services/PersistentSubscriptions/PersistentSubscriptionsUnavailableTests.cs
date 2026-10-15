// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.PersistentSubscription;
using KurrentDB.Core.Tests.Services.Replication;
using Xunit;
using OperationStatus = KurrentDB.Core.Messages.MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus;

namespace KurrentDB.Core.XUnit.Tests.Services.PersistentSubscriptions;

// With persistent subscriptions switched off, every request that still reaches the bus must be answered: the callers
// that remain wait on the reply, some without a timeout.
public class PersistentSubscriptionsUnavailableTests {
	readonly SynchronousScheduler _bus = new();
	readonly FakeEnvelope _envelope = new();

	public PersistentSubscriptionsUnavailableTests() => PersistentSubscriptionsUnavailable.SubscribeTo(_bus);

	T SingleReply<T>() where T : Message => Assert.IsType<T>(Assert.Single(_envelope.Replies));

	[Fact]
	public void stats_requests_fail_with_the_reason() {
		_bus.Publish(new MonitoringMessage.GetAllPersistentSubscriptionStats(_envelope));
		_bus.Publish(new MonitoringMessage.GetStreamPersistentSubscriptionStats(_envelope, "stream"));
		_bus.Publish(new MonitoringMessage.GetPersistentSubscriptionStats(_envelope, "stream", "group"));

		Assert.Equal(3, _envelope.Replies.Count);
		Assert.All(_envelope.Replies, reply => {
			var completed = Assert.IsType<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>(reply);
			Assert.Equal(OperationStatus.Fail, completed.Result);
			Assert.Equal(PersistentSubscriptionsUnavailable.Reason, completed.ErrorString);
		});
	}

	[Fact]
	public void a_restart_is_refused() {
		_bus.Publish(new SubscriptionMessage.PersistentSubscriptionsRestart(_envelope));

		Assert.Equal(PersistentSubscriptionsUnavailable.Reason,
			SingleReply<SubscriptionMessage.InvalidPersistentSubscriptionsRestart>().Reason);
	}

	[Fact]
	public void a_connection_is_dropped_as_not_found() {
		_bus.Publish(new ClientMessage.ConnectToPersistentSubscriptionToStream(Guid.NewGuid(), Guid.NewGuid(), _envelope,
			Guid.NewGuid(), "connection", "group", "stream", 10, string.Empty, null));

		Assert.Equal(SubscriptionDropReason.NotFound, SingleReply<ClientMessage.SubscriptionDropped>().Reason);
	}

	[Fact]
	public void creating_a_group_fails_with_the_reason() {
		_bus.Publish(new ClientMessage.CreatePersistentSubscriptionToStream(Guid.NewGuid(), Guid.NewGuid(), _envelope,
			"stream", "group", false, 0, 1000, false, 10, 10, 10, 10, 1000, 1, 10, 0, "RoundRobin", null));

		var completed = SingleReply<ClientMessage.CreatePersistentSubscriptionToStreamCompleted>();
		Assert.Equal(ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult.Fail, completed.Result);
		Assert.Equal(PersistentSubscriptionsUnavailable.Reason, completed.Reason);
	}

	[Fact]
	public void deleting_a_group_fails() {
		_bus.Publish(new ClientMessage.DeletePersistentSubscriptionToAll(Guid.NewGuid(), Guid.NewGuid(), _envelope, "group", null));

		Assert.Equal(ClientMessage.DeletePersistentSubscriptionToAllCompleted.DeletePersistentSubscriptionToAllResult.Fail,
			SingleReply<ClientMessage.DeletePersistentSubscriptionToAllCompleted>().Result);
	}

	[Fact]
	public void parked_message_requests_fail() {
		_bus.Publish(new ClientMessage.ReplayParkedMessages(Guid.NewGuid(), Guid.NewGuid(), _envelope, "stream", "group", null, null));
		_bus.Publish(new ClientMessage.TruncateParkedMessages(Guid.NewGuid(), Guid.NewGuid(), _envelope, "stream", "group", null, null));

		Assert.Collection(_envelope.Replies,
			reply => Assert.Equal(ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Fail,
				Assert.IsType<ClientMessage.ReplayMessagesReceived>(reply).Result),
			reply => Assert.Equal(ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.Fail,
				Assert.IsType<ClientMessage.TruncateParkedMessagesCompleted>(reply).Result));
	}

	[Fact]
	public void reading_the_next_messages_fails() {
		_bus.Publish(new ClientMessage.ReadNextNPersistentMessages(Guid.NewGuid(), Guid.NewGuid(), _envelope, "stream", "group", 10, null));

		Assert.Equal(ClientMessage.ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult.Fail,
			SingleReply<ClientMessage.ReadNextNPersistentMessagesCompleted>().Result);
	}
}
