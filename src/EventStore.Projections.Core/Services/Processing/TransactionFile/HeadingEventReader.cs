// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Services.Processing.TransactionFile;

public partial class HeadingEventReader {
	private IEventReader _headEventReader;
	private TFPos _subscribeFromPosition = new TFPos(long.MaxValue, long.MaxValue);

	private readonly Queue<HeadingEventReader.Item> _lastMessages = new Queue<HeadingEventReader.Item>();

	private readonly int _eventCacheSize;

	private readonly Dictionary<Guid, IReaderSubscription> _headSubscribers =
		new Dictionary<Guid, IReaderSubscription>();

	private Guid _eventReaderId;

	private bool _started;

	private TFPos _lastEventPosition = new TFPos(0, -1);
	private TFPos _lastDeletePosition = new TFPos(0, -1);

	private IPublisher _publisher;

	public HeadingEventReader(int eventCacheSize, IPublisher publisher) {
		_eventCacheSize = eventCacheSize;
		_publisher = publisher;
	}

	public bool Handle(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		EnsureStarted();
		if (message.CorrelationId != _eventReaderId)
			return false;
		if (message.Data == null)
			return true;

		ValidateEventOrder(message);

		CacheRecentMessage(message);
		DistributeMessage(message);
		return true;
	}

	public bool Handle(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		EnsureStarted();
		if (message.CorrelationId != _eventReaderId)
			return false;

		ValidateEventOrder(message);


		CacheRecentMessage(message);
		DistributeMessage(message);
		return true;
	}

	public bool Handle(ReaderSubscriptionMessage.EventReaderIdle message) {
		EnsureStarted();
		if (message.CorrelationId != _eventReaderId)
			return false;
		DistributeMessage(message);
		return true;
	}

	private void ValidateEventOrder(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		if (_lastEventPosition >= message.Data.Position || _lastDeletePosition > message.Data.Position)
			throw new InvalidOperationException(
				string.Format(
					"Invalid committed event order.  Last: '{0}' Received: '{1}'  LastDelete: '{2}'",
					_lastEventPosition, message.Data.Position, _lastEventPosition));
		_lastEventPosition = message.Data.Position;
	}

	private void ValidateEventOrder(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		if (_lastEventPosition > message.DeleteLinkOrEventPosition.Value
		    || _lastDeletePosition >= message.DeleteLinkOrEventPosition.Value)
			throw new InvalidOperationException(
				string.Format(
					"Invalid partition deleted event order.  Last: '{0}' Received: '{1}'  LastDelete: '{2}'",
					_lastEventPosition, message.DeleteLinkOrEventPosition.Value, _lastEventPosition));
		_lastDeletePosition = message.DeleteLinkOrEventPosition.Value;
	}

	public void Start(Guid eventReaderId, IEventReader eventReader) {
		if (_started)
			throw new InvalidOperationException("Already started");
		_eventReaderId = eventReaderId;
		_headEventReader = eventReader;
		//Guid.Empty means head distribution point
		_started = true; // started before resume due to old style test with immediate callback
		_headEventReader.Resume();
	}

	public void Stop() {
		EnsureStarted();
		_headEventReader.Pause();
		_headEventReader = null;
		EmptyCache();
		_started = false;
	}

	public bool TrySubscribe(
		Guid projectionId, IReaderSubscription readerSubscription, long fromTransactionFilePosition) {
		EnsureStarted();
		if (_headSubscribers.ContainsKey(projectionId))
			throw new InvalidOperationException(
				string.Format("Projection '{0}' has been already subscribed", projectionId));
		if (_subscribeFromPosition.CommitPosition <= fromTransactionFilePosition) {
			if (!DispatchRecentMessagesTo(readerSubscription, fromTransactionFilePosition)) {
				return false;
			}

			AddSubscriber(projectionId, readerSubscription);
			return true;
		}

		return false;
	}

	public void Unsubscribe(Guid projectionId) {
		EnsureStarted();
		if (!_headSubscribers.ContainsKey(projectionId))
			throw new InvalidOperationException(
				string.Format("Projection '{0}' has not been subscribed", projectionId));
		_headSubscribers.Remove(projectionId);
	}

	private bool DispatchRecentMessagesTo(IReaderSubscription subscription, long fromTransactionFilePosition) {
		foreach (var m in _lastMessages) {
			if (m.Position.CommitPosition >= fromTransactionFilePosition) {
				try {
					m.Handle(subscription);
				} catch (Exception ex) {
					var item = m as HeadingEventReader.CommittedEventItem;
					string message;
					if (item != null) {
						message = string.Format(
							"The heading subscription failed to handle a recently cached event {0}:{1}@{2} because {3}",
							item.Message.Data.EventStreamId, item.Message.Data.EventType,
							item.Message.Data.PositionSequenceNumber, ex.Message);
					} else {
						message = string.Format(
							"The heading subscription failed to handle a recently cached deleted event at position {0} because {1}",
							m.Position, ex.Message);
					}

					_publisher.Publish(
						new EventReaderSubscriptionMessage.Failed(subscription.SubscriptionId, message));
					return false;
				}
			}
		}

		return true;
	}

	private void DistributeMessage(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		foreach (var subscriber in _headSubscribers.Values) {
			try {
				subscriber.Handle(message);
			} catch (Exception ex) {
				_publisher.Publish(new EventReaderSubscriptionMessage.Failed(subscriber.SubscriptionId,
					string.Format("The heading subscription failed to handle an event {0}:{1}@{2} because {3}",
						message.Data.EventStreamId, message.Data.EventType, message.Data.PositionSequenceNumber,
						ex.Message)));
			}
		}
	}

	private void DistributeMessage(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		foreach (var subscriber in _headSubscribers.Values)
			subscriber.Handle(message);
	}

	private void DistributeMessage(ReaderSubscriptionMessage.EventReaderIdle message) {
		foreach (var subscriber in _headSubscribers.Values)
			subscriber.Handle(message);
	}

	private void CacheRecentMessage(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		_lastMessages.Enqueue(new HeadingEventReader.CommittedEventItem(message));
		CleanUpCache();
	}

	private void CacheRecentMessage(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		_lastMessages.Enqueue(new HeadingEventReader.PartitionDeletedItem(message));
		CleanUpCache();
	}

	private void CleanUpCache() {
		if (_lastMessages.Count > _eventCacheSize) {
			var removed = _lastMessages.Dequeue();
			// as we may have multiple items at the same position it is important to
			// remove them together as we may subscribe in the middle otherwise
			while (_lastMessages.Count > 0 && _lastMessages.Peek().Position == removed.Position)
				_lastMessages.Dequeue();
		}

		var lastAvailableCommittedEvent = _lastMessages.Peek();
		_subscribeFromPosition = lastAvailableCommittedEvent.Position;
	}

	private void EmptyCache() {
		_lastMessages.Clear();
		_subscribeFromPosition = new TFPos(long.MaxValue, long.MaxValue);
	}

	private void AddSubscriber(Guid publishWithCorrelationId, IReaderSubscription subscription) {
		_headSubscribers.Add(publishWithCorrelationId, subscription);
	}

	private void EnsureStarted() {
		if (!_started)
			throw new InvalidOperationException("Not started");
	}
}
