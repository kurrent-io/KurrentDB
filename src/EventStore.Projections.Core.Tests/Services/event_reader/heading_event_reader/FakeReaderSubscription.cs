// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Tests.Services.event_reader.heading_event_reader;

public class FakeReaderSubscription : IReaderSubscription {
	private readonly IPublisher _publisher;
	private readonly Guid _subscriptionId;

	public FakeReaderSubscription() {
		_subscriptionId = Guid.NewGuid();
	}

	public FakeReaderSubscription(IPublisher publisher, Guid subscriptionId) {
		_publisher = publisher;
		_subscriptionId = subscriptionId;
	}
	private readonly List<ReaderSubscriptionMessage.CommittedEventDistributed> _receivedEvents =
		new List<ReaderSubscriptionMessage.CommittedEventDistributed>();

	private readonly List<ReaderSubscriptionMessage.EventReaderIdle> _receivedIdleNotifications =
		new List<ReaderSubscriptionMessage.EventReaderIdle>();

	private readonly List<ReaderSubscriptionMessage.EventReaderStarting> _receivedStartingNotifications =
		new List<ReaderSubscriptionMessage.EventReaderStarting>();

	private readonly List<ReaderSubscriptionMessage.EventReaderEof> _receivedEofNotifications =
		new List<ReaderSubscriptionMessage.EventReaderEof>();

	private readonly List<ReaderSubscriptionMessage.EventReaderPartitionEof> _receivedPartitionEofNotifications =
		new List<ReaderSubscriptionMessage.EventReaderPartitionEof>();

	private readonly List<ReaderSubscriptionMessage.EventReaderPartitionDeleted>
		_receivedPartitionDeletedNotifications =
			new List<ReaderSubscriptionMessage.EventReaderPartitionDeleted>();

	private readonly List<ReaderSubscriptionMessage.EventReaderNotAuthorized> _receivedNotAuthorizedNotifications =
		new List<ReaderSubscriptionMessage.EventReaderNotAuthorized>();

	public void Handle(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		if (message.Data != null && message.Data.PositionStreamId == "throws") {
			throw new Exception("Bad Handler");
		}

		_receivedEvents.Add(message);
		_publisher?.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived
				.FromCommittedEventDistributed(message,
				CheckpointTag.Empty, "", _subscriptionId, 0));
	}

	public List<ReaderSubscriptionMessage.CommittedEventDistributed> ReceivedEvents {
		get { return _receivedEvents; }
	}

	public List<ReaderSubscriptionMessage.EventReaderIdle> ReceivedIdleNotifications {
		get { return _receivedIdleNotifications; }
	}

	public List<ReaderSubscriptionMessage.EventReaderStarting> ReceivedStartingNotifications {
		get { return _receivedStartingNotifications; }
	}

	public List<ReaderSubscriptionMessage.EventReaderEof> ReceivedEofNotifications {
		get { return _receivedEofNotifications; }
	}

	public List<ReaderSubscriptionMessage.EventReaderPartitionEof> ReceivedPartitionEofNotifications {
		get { return _receivedPartitionEofNotifications; }
	}

	public List<ReaderSubscriptionMessage.EventReaderPartitionDeleted> ReceivedPartitionDeletedNotifications {
		get { return _receivedPartitionDeletedNotifications; }
	}

	public List<ReaderSubscriptionMessage.EventReaderNotAuthorized> ReceivedNotAuthorizedNotifications {
		get { return _receivedNotAuthorizedNotifications; }
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderIdle message) {
		_receivedIdleNotifications.Add(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderStarting message) {
		_receivedStartingNotifications.Add(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderEof message) {
		_receivedEofNotifications.Add(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderPartitionEof message) {
		_receivedPartitionEofNotifications.Add(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		_receivedPartitionDeletedNotifications.Add(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderNotAuthorized message) {
		_receivedNotAuthorizedNotifications.Add(message);
	}

	public void Handle(ReaderSubscriptionMessage.Faulted message) {
		//ignore
	}

	public void Handle(ReaderSubscriptionMessage.ReportProgress message) {
		//ignore
	}

	public string Tag {
		get { return "FakeReaderSubscription"; }
	}

	public Guid SubscriptionId {
		get { return Guid.Empty; }
	}

	private FakeEventReader _eventReader;

	public FakeEventReader EventReader {
		get { return _eventReader; }
	}

	public IEventReader CreatePausedEventReader(
		IPublisher publisher, IODispatcher ioDispatcher, Guid forkedEventReaderId) {
		_eventReader = new FakeEventReader(forkedEventReaderId);
		return _eventReader;
	}
}

public class FakeEventReader : IEventReader {
	public Guid EventReaderId { get; private set; }

	public FakeEventReader(Guid eventReaderId) {
		EventReaderId = eventReaderId;
	}

	public void Dispose() {
	}

	public void Pause() {
	}

	public void Resume() {
	}

	public void SendNotAuthorized() {
	}
}

public class FakeReaderStrategy : IReaderStrategy {
	public EventFilter EventFilter { get; set; }
	public bool IsReadingOrderRepeatable { get; set; }
	public PositionTagger PositionTagger { get; set; }
	private FakeReaderSubscription _subscription;

	public Guid EventReaderId {
		get { return _subscription.EventReader.EventReaderId; }
	}

	public IEventReader CreatePausedEventReader(Guid eventReaderId, IPublisher publisher, IODispatcher ioDispatcher,
		CheckpointTag checkpointTag, bool stopOnEof, int? stopAfterNEvents) {
		throw new NotImplementedException();
	}

	public IReaderSubscription CreateReaderSubscription(IPublisher publisher, CheckpointTag fromCheckpointTag,
		Guid subscriptionId, ReaderSubscriptionOptions readerSubscriptionOptions) {
		_subscription = new FakeReaderSubscription(publisher, subscriptionId);
		return _subscription;
	}
}
