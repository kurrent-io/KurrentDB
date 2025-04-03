// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Data;

namespace EventStore.Core.Services.PersistentSubscription;

public struct OutstandingMessage {
	public readonly ResolvedEvent ResolvedEvent;
	public readonly int RetryCount;
	public readonly Guid EventId;
	public readonly bool IsReplayedEvent;
	public readonly long? EventSequenceNumber;
	public readonly IPersistentSubscriptionStreamPosition EventPosition;
	public readonly IPersistentSubscriptionStreamPosition PreviousEventPosition;

	private OutstandingMessage(Guid eventId, ResolvedEvent resolvedEvent, int retryCount, bool isReplayedEvent, long? eventSequenceNumber, IPersistentSubscriptionStreamPosition eventPosition, IPersistentSubscriptionStreamPosition previousEventPosition) : this() {
		EventId = eventId;
		ResolvedEvent = resolvedEvent;
		RetryCount = retryCount;
		IsReplayedEvent = isReplayedEvent;
		EventSequenceNumber = eventSequenceNumber;
		EventPosition = eventPosition;
		PreviousEventPosition = previousEventPosition;
	}

	public static OutstandingMessage ForNewEvent(ResolvedEvent resolvedEvent, IPersistentSubscriptionStreamPosition eventPosition) {
		Ensure.NotNull(eventPosition, "eventPosition");
		return new OutstandingMessage(resolvedEvent.OriginalEvent.EventId, resolvedEvent, 0, false, null, eventPosition, null);
	}

	public static OutstandingMessage ForParkedEvent(ResolvedEvent resolvedEvent) {
		return new OutstandingMessage(resolvedEvent.OriginalEvent.EventId, resolvedEvent, 0, true, null, null, null);
	}

	public static (OutstandingMessage message, bool newSequenceNumberAssigned) ForPushedEvent(OutstandingMessage message, long nextSequenceNumber, IPersistentSubscriptionStreamPosition previousEventPosition) {
		if (nextSequenceNumber > 0) {
			//only the first event may have a null previous event position
			Ensure.NotNull(previousEventPosition, nameof(previousEventPosition));
		}

		if (message.IsReplayedEvent) { //replayed parked message
			return (message, false);
		}

		if (message.EventSequenceNumber.HasValue) { //retried message
			return (message, false);
		}

		return (new OutstandingMessage( //new event
			message.EventId,
			message.ResolvedEvent,
			message.RetryCount,
			message.IsReplayedEvent,
			nextSequenceNumber,
			message.EventPosition,
			previousEventPosition), true);
	}

	public static OutstandingMessage ForRetriedEvent(OutstandingMessage message) {
		return new OutstandingMessage(
			message.EventId,
			message.ResolvedEvent,
			message.RetryCount + 1,
			message.IsReplayedEvent,
			message.EventSequenceNumber,
			message.EventPosition,
			message.PreviousEventPosition);
	}
}
