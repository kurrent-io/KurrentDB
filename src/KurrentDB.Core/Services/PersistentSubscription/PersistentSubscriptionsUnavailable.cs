// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.PersistentSubscription;

/// <summary>
/// Answers persistent subscription requests when <see cref="PersistentSubscriptionsFeature"/> is off. Callers that
/// remain (the operations endpoint, description documents, the TCP protocol, the admin UI) wait on a reply, and
/// without one they would wait until their own timeouts, or for ever.
/// </summary>
public sealed class PersistentSubscriptionsUnavailable :
	IHandle<ClientMessage.CreatePersistentSubscriptionToStream>,
	IHandle<ClientMessage.UpdatePersistentSubscriptionToStream>,
	IHandle<ClientMessage.DeletePersistentSubscriptionToStream>,
	IHandle<ClientMessage.CreatePersistentSubscriptionToAll>,
	IHandle<ClientMessage.UpdatePersistentSubscriptionToAll>,
	IHandle<ClientMessage.DeletePersistentSubscriptionToAll>,
	IHandle<ClientMessage.ConnectToPersistentSubscriptionToStream>,
	IHandle<ClientMessage.ConnectToPersistentSubscriptionToAll>,
	IHandle<ClientMessage.ReadNextNPersistentMessages>,
	IHandle<ClientMessage.ReplayParkedMessages>,
	IHandle<ClientMessage.ReplayParkedMessage>,
	IHandle<ClientMessage.TruncateParkedMessages>,
	IHandle<MonitoringMessage.GetAllPersistentSubscriptionStats>,
	IHandle<MonitoringMessage.GetStreamPersistentSubscriptionStats>,
	IHandle<MonitoringMessage.GetPersistentSubscriptionStats>,
	IHandle<SubscriptionMessage.PersistentSubscriptionsRestart> {

	public const string Reason = "Persistent subscriptions are switched off on this node.";

	public static void SubscribeTo(ISubscriber bus) {
		var handler = new PersistentSubscriptionsUnavailable();
		bus.Subscribe<ClientMessage.CreatePersistentSubscriptionToStream>(handler);
		bus.Subscribe<ClientMessage.UpdatePersistentSubscriptionToStream>(handler);
		bus.Subscribe<ClientMessage.DeletePersistentSubscriptionToStream>(handler);
		bus.Subscribe<ClientMessage.CreatePersistentSubscriptionToAll>(handler);
		bus.Subscribe<ClientMessage.UpdatePersistentSubscriptionToAll>(handler);
		bus.Subscribe<ClientMessage.DeletePersistentSubscriptionToAll>(handler);
		bus.Subscribe<ClientMessage.ConnectToPersistentSubscriptionToStream>(handler);
		bus.Subscribe<ClientMessage.ConnectToPersistentSubscriptionToAll>(handler);
		bus.Subscribe<ClientMessage.ReadNextNPersistentMessages>(handler);
		bus.Subscribe<ClientMessage.ReplayParkedMessages>(handler);
		bus.Subscribe<ClientMessage.ReplayParkedMessage>(handler);
		bus.Subscribe<ClientMessage.TruncateParkedMessages>(handler);
		bus.Subscribe<MonitoringMessage.GetAllPersistentSubscriptionStats>(handler);
		bus.Subscribe<MonitoringMessage.GetStreamPersistentSubscriptionStats>(handler);
		bus.Subscribe<MonitoringMessage.GetPersistentSubscriptionStats>(handler);
		bus.Subscribe<SubscriptionMessage.PersistentSubscriptionsRestart>(handler);
	}

	public void Handle(ClientMessage.CreatePersistentSubscriptionToStream message) =>
		message.Envelope.ReplyWith(new ClientMessage.CreatePersistentSubscriptionToStreamCompleted(message.CorrelationId,
			ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult.Fail, Reason));

	public void Handle(ClientMessage.UpdatePersistentSubscriptionToStream message) =>
		message.Envelope.ReplyWith(new ClientMessage.UpdatePersistentSubscriptionToStreamCompleted(message.CorrelationId,
			ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult.Fail, Reason));

	public void Handle(ClientMessage.DeletePersistentSubscriptionToStream message) =>
		message.Envelope.ReplyWith(new ClientMessage.DeletePersistentSubscriptionToStreamCompleted(message.CorrelationId,
			ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult.Fail, Reason));

	public void Handle(ClientMessage.CreatePersistentSubscriptionToAll message) =>
		message.Envelope.ReplyWith(new ClientMessage.CreatePersistentSubscriptionToAllCompleted(message.CorrelationId,
			ClientMessage.CreatePersistentSubscriptionToAllCompleted.CreatePersistentSubscriptionToAllResult.Fail, Reason));

	public void Handle(ClientMessage.UpdatePersistentSubscriptionToAll message) =>
		message.Envelope.ReplyWith(new ClientMessage.UpdatePersistentSubscriptionToAllCompleted(message.CorrelationId,
			ClientMessage.UpdatePersistentSubscriptionToAllCompleted.UpdatePersistentSubscriptionToAllResult.Fail, Reason));

	public void Handle(ClientMessage.DeletePersistentSubscriptionToAll message) =>
		message.Envelope.ReplyWith(new ClientMessage.DeletePersistentSubscriptionToAllCompleted(message.CorrelationId,
			ClientMessage.DeletePersistentSubscriptionToAllCompleted.DeletePersistentSubscriptionToAllResult.Fail, Reason));

	// The service's own answer to a group that does not exist.
	public void Handle(ClientMessage.ConnectToPersistentSubscriptionToStream message) =>
		message.Envelope.ReplyWith(new ClientMessage.SubscriptionDropped(message.CorrelationId, SubscriptionDropReason.NotFound));

	public void Handle(ClientMessage.ConnectToPersistentSubscriptionToAll message) =>
		message.Envelope.ReplyWith(new ClientMessage.SubscriptionDropped(message.CorrelationId, SubscriptionDropReason.NotFound));

	public void Handle(ClientMessage.ReadNextNPersistentMessages message) =>
		message.Envelope.ReplyWith(new ClientMessage.ReadNextNPersistentMessagesCompleted(message.CorrelationId,
			ClientMessage.ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult.Fail, Reason, null));

	public void Handle(ClientMessage.ReplayParkedMessages message) =>
		message.Envelope.ReplyWith(new ClientMessage.ReplayMessagesReceived(message.CorrelationId,
			ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Fail, Reason));

	public void Handle(ClientMessage.ReplayParkedMessage message) =>
		message.Envelope.ReplyWith(new ClientMessage.ReplayMessagesReceived(message.CorrelationId,
			ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Fail, Reason));

	public void Handle(ClientMessage.TruncateParkedMessages message) =>
		message.Envelope.ReplyWith(new ClientMessage.TruncateParkedMessagesCompleted(message.CorrelationId,
			ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.Fail, Reason));

	public void Handle(MonitoringMessage.GetAllPersistentSubscriptionStats message) =>
		message.Envelope.ReplyWith(StatsUnavailable());

	public void Handle(MonitoringMessage.GetStreamPersistentSubscriptionStats message) =>
		message.Envelope.ReplyWith(StatsUnavailable());

	public void Handle(MonitoringMessage.GetPersistentSubscriptionStats message) =>
		message.Envelope.ReplyWith(StatsUnavailable());

	public void Handle(SubscriptionMessage.PersistentSubscriptionsRestart message) =>
		message.ReplyEnvelope.ReplyWith(new SubscriptionMessage.InvalidPersistentSubscriptionsRestart(Reason));

	static MonitoringMessage.GetPersistentSubscriptionStatsCompleted StatsUnavailable() =>
		new(MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Fail, null, Reason);
}
