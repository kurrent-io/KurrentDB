// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Common;

namespace EventStore.Core.Services.Transport.Enumerators;

public abstract class ReadResponse {
	public class EventReceived : ReadResponse {
		public ResolvedEvent Event;

		public EventReceived(ResolvedEvent @event) {
			Event = @event;
		}
	}

	public class SubscriptionCaughtUp : ReadResponse { }

	public class SubscriptionFellBehind : ReadResponse { }

	public class CheckpointReceived : ReadResponse {
		public readonly ulong CommitPosition;
		public readonly ulong PreparePosition;

		public CheckpointReceived(ulong commitPosition, ulong preparePosition) {
			CommitPosition = commitPosition;
			PreparePosition = preparePosition;
		}
	}

	public class StreamNotFound : ReadResponse {
		public readonly string StreamName;

		public StreamNotFound(string streamName) {
			StreamName = streamName;
		}
	}

	public class SubscriptionConfirmed : ReadResponse {
		public readonly string SubscriptionId;

		public SubscriptionConfirmed(string subscriptionId) {
			SubscriptionId = subscriptionId;
		}
	}

	public class LastStreamPositionReceived : ReadResponse {
		public readonly StreamRevision LastStreamPosition;

		public LastStreamPositionReceived(StreamRevision lastStreamPosition) {
			LastStreamPosition = lastStreamPosition;
		}
	}

	public class FirstStreamPositionReceived : ReadResponse {
		public readonly StreamRevision FirstStreamPosition;

		public FirstStreamPositionReceived(StreamRevision firstStreamPosition) {
			FirstStreamPosition = firstStreamPosition;
		}
	}
}
