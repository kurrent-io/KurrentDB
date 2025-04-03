// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Settings;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services;

public sealed class ReaderSubscriptionDispatcher {
	private readonly ConcurrentDictionary<Guid, object> _map = new();
	private readonly IPublisher _publisher;
	private readonly IEnvelope _publishEnvelope;
	private readonly TimeSpan _readerSubscriptionTimeout =
		TimeSpan.FromMilliseconds(ESConsts.ReadRequestTimeout);

	public ReaderSubscriptionDispatcher(IPublisher publisher) {
		_publisher = publisher;
		_publishEnvelope = _publisher;
	}

	/// <summary>
	/// Publishes the <see cref="ReaderSubscriptionManagement.Subscribe"/> message to be handled later.
	/// The caller must wait for further <see cref="EventReaderSubscriptionMessage"/>s to determine whether
	/// the subscription was successful.
	/// </summary>
	/// <param name="request">The subscription to request</param>
	/// <param name="subscriber">The subscriber to call with any responses for this subscription</param>
	/// <param name="scheduleTimeout">Whether to schedule a <see cref="EventReaderSubscriptionMessage.SubscribeTimeout"/> message for this subscription</param>
	/// <returns></returns>
	public void PublishSubscribe(
		ReaderSubscriptionManagement.Subscribe request, object subscriber, bool scheduleTimeout) {
		_map.TryAdd(request.SubscriptionId, subscriber);
		_publisher.Publish(request);
		if (scheduleTimeout)
			ScheduleSubscriptionTimeout(request.SubscriptionId);
	}

	private void ScheduleSubscriptionTimeout(Guid subscriptionId) {
		_publisher.Publish(TimerMessage.Schedule.Create(
			_readerSubscriptionTimeout, _publishEnvelope,
			new EventReaderSubscriptionMessage.SubscribeTimeout(subscriptionId)));
	}

	public void Cancel(Guid requestId) {
		_map.TryRemove(requestId, out _);
	}

	public IHandle<T> CreateSubscriber<T>() where T : EventReaderSubscriptionMessageBase {
		return new Subscriber<T>(this);
	}

	private void Handle<T>(T message) where T : EventReaderSubscriptionMessageBase {
		var correlationId = message.SubscriptionId;
		if (_map.TryGetValue(correlationId, out var subscriber)) {
			if (subscriber is IHandle<T> h) {
				h.Handle(message);
			} else if (subscriber is IAsyncHandle<T>) {
				throw new Exception($"ReaderSubscriptionDispatcher does not support asynchronous subscribers. Subscriber: {subscriber}");
			}
		}
	}

	public void Subscribed(Guid correlationId, object subscriber) {
		_map.TryAdd(correlationId, subscriber);
	}

	private class Subscriber<T> : IHandle<T> where T : EventReaderSubscriptionMessageBase {
		private readonly ReaderSubscriptionDispatcher _host;

		public Subscriber(
			ReaderSubscriptionDispatcher host) {
			_host = host;
		}

		public void Handle(T message) {
			_host.Handle(message);
		}
	}
}
