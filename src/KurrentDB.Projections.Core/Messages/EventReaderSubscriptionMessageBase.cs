// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Messages;

public static partial class EventReaderSubscriptionMessage {
	/// <summary>
	/// A CheckpointSuggested message is sent to core projection
	/// to allow bookmarking a position that can be used to
	/// restore the projection processing (typically
	/// an event at this position does not satisfy projection filter)
	/// </summary>
	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public partial class CheckpointSuggested(
		Guid subscriptionId,
		CheckpointTag checkpointTag,
		float progress,
		long subscriptionMessageSequenceNumber,
		object source = null)
		: EventReaderSubscriptionMessageBase(subscriptionId, checkpointTag, progress, subscriptionMessageSequenceNumber, source);

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public partial class ProgressChanged(
		Guid subscriptionId,
		CheckpointTag checkpointTag,
		float progress,
		long subscriptionMessageSequenceNumber,
		object source = null)
		: EventReaderSubscriptionMessageBase(subscriptionId, checkpointTag, progress, subscriptionMessageSequenceNumber, source);

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public partial class SubscriptionStarted(
		Guid subscriptionId,
		CheckpointTag checkpointTag,
		long startingLastCommitPosition,
		long subscriptionMessageSequenceNumber,
		object source = null)
		: EventReaderSubscriptionMessageBase(subscriptionId, checkpointTag, 0f, subscriptionMessageSequenceNumber, source) {
		public long StartingLastCommitPosition { get; } = startingLastCommitPosition;
	}

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public sealed partial class NotAuthorized(
		Guid subscriptionId,
		CheckpointTag checkpointTag,
		float progress,
		long subscriptionMessageSequenceNumber,
		object source = null)
		: EventReaderSubscriptionMessageBase(subscriptionId, checkpointTag, progress, subscriptionMessageSequenceNumber, source);

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public sealed partial class SubscribeTimeout(Guid subscriptionId)
		: EventReaderSubscriptionMessageBase(subscriptionId, CheckpointTag.Empty, 100.0f, -1, null);

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public sealed partial class Failed(Guid subscriptionId, string reason)
		: EventReaderSubscriptionMessageBase(subscriptionId, CheckpointTag.Empty, 100.0f, -1, null) {
		public string Reason { get; } = reason;
	}

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public partial class EofReached(
		Guid subscriptionId,
		CheckpointTag checkpointTag,
		long subscriptionMessageSequenceNumber,
		object source = null)
		: EventReaderSubscriptionMessageBase(subscriptionId, checkpointTag, 100.0f, subscriptionMessageSequenceNumber, source);

	/// <summary>
	/// NOTE: the PartitionDeleted may appear out-of-order and is not guaranteed
	/// to appear at the same sequence position in a recovery
	/// </summary>
	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public partial class PartitionDeleted(
		Guid subscriptionId,
		CheckpointTag checkpointTag,
		string partition,
		long subscriptionMessageSequenceNumber,
		object source = null)
		: EventReaderSubscriptionMessageBase(subscriptionId, checkpointTag, 100.0f, subscriptionMessageSequenceNumber, source) {
		public string Partition { get; } = partition;
	}

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public partial class CommittedEventReceived : EventReaderSubscriptionMessageBase {
		private CommittedEventReceived(
			Guid subscriptionId, CheckpointTag checkpointTag, string eventCategory, ResolvedEvent data,
			float progress, long subscriptionMessageSequenceNumber, object source)
			: base(subscriptionId, checkpointTag, progress, subscriptionMessageSequenceNumber, source) {
			Data = Ensure.NotNull(data);
			EventCategory = eventCategory;
		}

		public static CommittedEventReceived Sample(
			ResolvedEvent data, Guid subscriptionId, long subscriptionMessageSequenceNumber) {
			return new(
				subscriptionId, 0, null, data, 77.7f, subscriptionMessageSequenceNumber);
		}

		public static CommittedEventReceived Sample(
			ResolvedEvent data, CheckpointTag checkpointTag, Guid subscriptionId,
			long subscriptionMessageSequenceNumber) {
			return new(
				subscriptionId, checkpointTag, null, data, 77.7f, subscriptionMessageSequenceNumber, null);
		}

		private CommittedEventReceived(
			Guid subscriptionId, int phase, string eventCategory, ResolvedEvent data, float progress,
			long subscriptionMessageSequenceNumber)
			: this(
				subscriptionId,
				CheckpointTag.FromPosition(phase, data.Position.CommitPosition, data.Position.PreparePosition),
				eventCategory, data, progress, subscriptionMessageSequenceNumber, null) {
		}

		public ResolvedEvent Data { get; }

		public string EventCategory { get; }

		public static CommittedEventReceived FromCommittedEventDistributed(
			ReaderSubscriptionMessage.CommittedEventDistributed message, CheckpointTag checkpointTag,
			string eventCategory, Guid subscriptionId, long subscriptionMessageSequenceNumber)
			=> new(
				subscriptionId, checkpointTag, eventCategory, message.Data, message.Progress,
				subscriptionMessageSequenceNumber, message.Source);

		public override string ToString() => CheckpointTag.ToString();
	}

	[DerivedMessage(ProjectionMessage.EventReaderSubscription)]
	public partial class ReaderAssignedReader(Guid subscriptionId, Guid readerId)
		: EventReaderSubscriptionMessageBase(subscriptionId, null, 0, 0, null) {
		public Guid ReaderId { get; } = readerId;
	}
}

[DerivedMessage]
public abstract partial class EventReaderSubscriptionMessageBase(
	Guid subscriptionId,
	CheckpointTag checkpointTag,
	float progress,
	long subscriptionMessageSequenceNumber,
	object source)
	: Message {
	public CheckpointTag CheckpointTag { get; } = checkpointTag;
	public float Progress { get; } = progress;
	public long SubscriptionMessageSequenceNumber { get; } = subscriptionMessageSequenceNumber;
	public Guid SubscriptionId { get; } = subscriptionId;
	public object Source { get; } = source;
}
