// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;

namespace KurrentDB.Core.Services.Transport.Enumerators;

public abstract class ReadResponse {
	public class EventReceived(ResolvedEvent @event) : ReadResponse {
		public ResolvedEvent Event = @event;
	}

	public class SubscriptionCaughtUp : ReadResponse {
		public readonly DateTime Timestamp;

		// Always populated for stream subscriptions
		public readonly long? StreamCheckpoint;

		// Always populated for $all subscriptions
		public readonly TFPos? AllCheckpoint;

		public SubscriptionCaughtUp(DateTime timestamp, long streamCheckpoint) {
			Timestamp = timestamp;
			StreamCheckpoint = streamCheckpoint;
		}

		public SubscriptionCaughtUp(DateTime timestamp, TFPos allCheckpoint) {
			Timestamp = timestamp;
			AllCheckpoint = allCheckpoint;
		}
	}

	public class SubscriptionFellBehind : ReadResponse {
		public readonly DateTime Timestamp;

		// Always populated for stream subscriptions
		public readonly long? StreamCheckpoint;

		// Always populated for $all subscriptions
		public readonly TFPos? AllCheckpoint;

		public SubscriptionFellBehind(DateTime timestamp, long streamCheckpoint) {
			Timestamp = timestamp;
			StreamCheckpoint = streamCheckpoint;
		}

		public SubscriptionFellBehind(DateTime timestamp, TFPos allCheckpoint) {
			Timestamp = timestamp;
			AllCheckpoint = allCheckpoint;
		}
	}

	public class CheckpointReceived(DateTime timestamp, ulong commitPosition, ulong preparePosition) : ReadResponse {
		public readonly DateTime Timestamp = timestamp;
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
