// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using static KurrentDB.Projections.Core.Messages.ReaderSubscriptionMessage;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.heading_event_reader;

public class FakeReaderSubscription(IPublisher publisher, Guid subscriptionId) : IReaderSubscription {
	public FakeReaderSubscription() : this(null, Guid.NewGuid()) {
	}

	public void Handle(CommittedEventDistributed message) {
		if (message.Data is { PositionStreamId: "throws" }) {
			throw new Exception("Bad Handler");
		}

		ReceivedEvents.Add(message);
		publisher?.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived
				.FromCommittedEventDistributed(message,
				CheckpointTag.Empty, "", subscriptionId, 0));
	}

	public List<CommittedEventDistributed> ReceivedEvents { get; } = [];

	public List<EventReaderIdle> ReceivedIdleNotifications { get; } = [];

	public void Handle(EventReaderIdle message) {
		ReceivedIdleNotifications.Add(message);
	}

	public void Handle(EventReaderStarting message) {
	}

	public void Handle(EventReaderEof message) {
	}

	public void Handle(EventReaderPartitionDeleted message) {
	}

	public void Handle(EventReaderNotAuthorized message) {
	}

	public void Handle(ReportProgress message) {
	}

	public Guid SubscriptionId { get => Guid.Empty; }

	public FakeEventReader EventReader { get; private set; }

	public IEventReader CreatePausedEventReader( IPublisher publisher, IODispatcher ioDispatcher, Guid forkedEventReaderId) {
		EventReader = new FakeEventReader(forkedEventReaderId);
		return EventReader;
	}
}

public class FakeEventReader(Guid eventReaderId) : IEventReader {
	public Guid EventReaderId { get; } = eventReaderId;

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

	public Guid EventReaderId => _subscription.EventReader.EventReaderId;

	public IEventReader CreatePausedEventReader(Guid eventReaderId, IPublisher publisher,
		CheckpointTag checkpointTag, bool stopOnEof) {
		throw new NotImplementedException();
	}

	public IReaderSubscription CreateReaderSubscription(IPublisher publisher, CheckpointTag fromCheckpointTag,
		Guid subscriptionId, ReaderSubscriptionOptions readerSubscriptionOptions) {
		_subscription = new FakeReaderSubscription(publisher, subscriptionId);
		return _subscription;
	}
}
