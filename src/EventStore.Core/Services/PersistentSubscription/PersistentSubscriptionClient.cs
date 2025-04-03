// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionClient {
	public readonly int MaximumInFlightMessages;

	private readonly Guid _correlationId;
	private readonly Guid _connectionId;
	private readonly string _connectionName;
	private readonly IEnvelope _envelope;
	private int _allowedMessages;
	public readonly string Username;
	public readonly string From;
	private long _totalItems;
	private readonly RequestStatistics _extraStatistics;
	private readonly Dictionary<Guid, OutstandingMessage> _unconfirmedEvents = new Dictionary<Guid, OutstandingMessage>();

	public PersistentSubscriptionClient(Guid correlationId,
		Guid connectionId,
		string connectionName,
		IEnvelope envelope,
		int inFlightMessages,
		string username,
		string from,
		Stopwatch watch,
		bool extraStatistics) {
		_correlationId = correlationId;
		_connectionId = connectionId;
		_connectionName = connectionName;
		_envelope = envelope;
		_allowedMessages = inFlightMessages;
		Username = username;
		From = @from;
		MaximumInFlightMessages = inFlightMessages;
		if (extraStatistics) {
			_extraStatistics = new RequestStatistics(watch, 1000);
		}
	}

	/// <summary>
	/// Raised whenever an in-flight event has been confirmed. This could be because of ack, nak, timeout or disconnection.
	/// </summary>
	public event Action<PersistentSubscriptionClient, ResolvedEvent> EventConfirmed;

	public int InflightMessages {
		get { return _unconfirmedEvents.Count; }
	}

	public int AvailableSlots {
		get { return _allowedMessages; }
	}

	public Guid ConnectionId {
		get { return _connectionId; }
	}

	public string ConnectionName {
		get { return _connectionName; }
	}

	public long TotalItems {
		get { return _totalItems; }
	}

	public long LastTotalItems { get; set; }

	public Guid CorrelationId {
		get { return _correlationId; }
	}

	internal bool RemoveFromProcessing(Guid[] processedEventIds) {
		bool removedAny = false;
		foreach (var processedEventId in processedEventIds) {
			if (_extraStatistics != null)
				_extraStatistics.EndOperation(processedEventId);
			OutstandingMessage ev;
			if (!_unconfirmedEvents.TryGetValue(processedEventId, out ev)) continue;
			_unconfirmedEvents.Remove(processedEventId);
			removedAny = true;
			_allowedMessages++;
			OnEventConfirmed(ev);
		}

		return removedAny;
	}

	public bool Push(OutstandingMessage message) {
		if (!CanSend()) {
			return false;
		}

		var evnt = message.ResolvedEvent;
		_allowedMessages--;
		Interlocked.Increment(ref _totalItems);
		if (_extraStatistics != null)
			_extraStatistics.StartOperation(evnt.OriginalEvent.EventId);

		_envelope.ReplyWith(
			new ClientMessage.PersistentSubscriptionStreamEventAppeared(CorrelationId, evnt, message.RetryCount));
		if (!_unconfirmedEvents.ContainsKey(evnt.OriginalEvent.EventId)) {
			_unconfirmedEvents.Add(evnt.OriginalEvent.EventId, message);
		}

		return true;
	}

	public IEnumerable<OutstandingMessage> GetUnconfirmedEvents() {
		return _unconfirmedEvents.Values;
	}

	internal void SendDropNotification() {
		_envelope.ReplyWith(
			new ClientMessage.SubscriptionDropped(CorrelationId, SubscriptionDropReason.Unsubscribed));
	}

	internal ObservedTimingMeasurement GetExtraStats() {
		return _extraStatistics == null ? null : _extraStatistics.GetMeasurementDetails();
	}

	private bool CanSend() {
		return AvailableSlots > 0;
	}

	private void OnEventConfirmed(OutstandingMessage ev) {
		var handler = EventConfirmed;
		if (handler != null) handler(this, ev.ResolvedEvent);
	}
}
