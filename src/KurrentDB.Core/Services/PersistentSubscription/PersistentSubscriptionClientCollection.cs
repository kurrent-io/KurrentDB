// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;

namespace KurrentDB.Core.Services.PersistentSubscription;

internal class PersistentSubscriptionClientCollection {
	private readonly IPersistentSubscriptionConsumerStrategy _consumerStrategy;

	private readonly Dictionary<Guid, PersistentSubscriptionClient> _hash =
		new Dictionary<Guid, PersistentSubscriptionClient>();

	public int Count {
		get { return _hash.Count; }
	}

	public PersistentSubscriptionClientCollection(IPersistentSubscriptionConsumerStrategy consumerStrategy) {
		_consumerStrategy = consumerStrategy;
	}

	public void AddClient(PersistentSubscriptionClient client) {
		_hash.Add(client.CorrelationId, client);
		_consumerStrategy.ClientAdded(client);
	}

	public ConsumerPushResult PushMessageToClient(OutstandingMessage message) {
		return _consumerStrategy.PushMessageToClient(message);
	}

	public bool RemoveClientByConnectionId(Guid connectionId,
		out IEnumerable<OutstandingMessage> unconfirmedEvents) {
		var clients = _hash.Values.Where(x => x.ConnectionId == connectionId).ToList();
		if (clients.Count == 0) {
			unconfirmedEvents = Array.Empty<OutstandingMessage>();
			return false;
		}

		unconfirmedEvents = clients.SelectMany(client => RemoveClientByCorrelationId(client.CorrelationId, false));
		return true;
	}

	public void ShutdownAll() {
		foreach (var client in _hash.Values.ToArray()) {
			RemoveClientByCorrelationId(client.CorrelationId, true);
		}
	}

	public IEnumerable<OutstandingMessage> RemoveClientByCorrelationId(Guid correlationId,
		bool sendDropNotification) {
		PersistentSubscriptionClient client;
		if (!_hash.TryGetValue(correlationId, out client))
			return new OutstandingMessage[0];
		_hash.Remove(client.CorrelationId);
		// A stopped client was already removed from the consumer strategy by StopClient.
		if (!client.IsStopped)
			_consumerStrategy.ClientRemoved(client);
		if (sendDropNotification) {
			client.SendDropNotification();
		}

		return client.GetUnconfirmedEvents();
	}

	// Removes the client from the consumer strategy so it stops receiving new
	// events, but keeps it in the hash so acks/nacks for in-flight events are
	// still delivered to it. Idempotent — calling Stop a second time is a no-op.
	public bool StopClient(Guid correlationId) {
		if (!_hash.TryGetValue(correlationId, out var client))
			return false;
		if (client.IsStopped)
			return true;
		client.MarkStopped();
		_consumerStrategy.ClientRemoved(client);
		return true;
	}

	public IEnumerable<PersistentSubscriptionClient> GetAll() {
		return _hash.Values;
	}

	public void RemoveProcessingMessages(params Guid[] processedEventIds) {
		foreach (var client in _hash.Values) {
			client.RemoveFromProcessing(processedEventIds);
		}
	}
}
