// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Common;

namespace EventStore.Core.Services.Transport.Enumerators;

public abstract class ReadResponse {
	public class EventReceived(ResolvedEvent @event) : ReadResponse {
		public ResolvedEvent Event = @event;
	}

	public class SubscriptionCaughtUp: ReadResponse;

	public class SubscriptionFellBehind: ReadResponse;

	public class CheckpointReceived(ulong commitPosition, ulong preparePosition) : ReadResponse {
		public readonly ulong CommitPosition = commitPosition;
		public readonly ulong PreparePosition = preparePosition;
	}

	public class StreamNotFound(string streamName) : ReadResponse {
		public readonly string StreamName = streamName;
	}

	public class SubscriptionConfirmed(string subscriptionId) : ReadResponse {
		public readonly string SubscriptionId = subscriptionId;
	}

	public class LastStreamPositionReceived(StreamRevision lastStreamPosition) : ReadResponse {
		public readonly StreamRevision LastStreamPosition = lastStreamPosition;
	}

	public class FirstStreamPositionReceived(StreamRevision firstStreamPosition) : ReadResponse {
		public readonly StreamRevision FirstStreamPosition = firstStreamPosition;
	}
}
