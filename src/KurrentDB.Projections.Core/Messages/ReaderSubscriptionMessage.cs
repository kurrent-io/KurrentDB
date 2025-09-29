// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Messages;

public static partial class ReaderSubscriptionMessage {
	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class SubscriptionMessage(Guid correlationId, object source) : Message {
		public Guid CorrelationId { get; } = correlationId;
		public object Source { get; } = source;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class EventReaderIdle(Guid correlationId, DateTime idleTimestampUtc, object source = null)
		: SubscriptionMessage(correlationId, source) {
		public DateTime IdleTimestampUtc { get; } = idleTimestampUtc;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public sealed partial class EventReaderStarting(Guid correlationId, long lastCommitPosition, object source = null)
		: SubscriptionMessage(correlationId, source) {
		public long LastCommitPosition { get; } = lastCommitPosition;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class EventReaderEof(Guid correlationId, object source = null) : SubscriptionMessage(correlationId, source);

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class EventReaderPartitionDeleted(
		Guid correlationId,
		string partition,
		TFPos? deleteLinkOrEventPosition,
		TFPos? deleteEventOrLinkTargetPosition,
		string positionStreamId,
		long? positionEventNumber,
		object source = null)
		: SubscriptionMessage(correlationId, source) {
		public string Partition { get; } = partition;
		public TFPos? DeleteEventOrLinkTargetPosition { get; } = deleteEventOrLinkTargetPosition;
		public string PositionStreamId { get; } = positionStreamId;
		public long? PositionEventNumber { get; } = positionEventNumber;
		public TFPos? DeleteLinkOrEventPosition { get; } = deleteLinkOrEventPosition;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public sealed partial class EventReaderNotAuthorized(Guid correlationId, object source = null)
		: SubscriptionMessage(correlationId, source);

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class CommittedEventDistributed(
		Guid correlationId,
		ResolvedEvent data,
		long? safeTransactionFileReaderJoinPosition,
		float progress,
		object source = null)
		: SubscriptionMessage(correlationId, source) {
		public static CommittedEventDistributed Sample(
			Guid correlationId, TFPos position, TFPos originalPosition, string positionStreamId,
			long positionSequenceNumber,
			string eventStreamId, long eventSequenceNumber, bool resolvedLinkTo, Guid eventId, string eventType,
			bool isJson, byte[] data, byte[] metadata, long? safeTransactionFileReaderJoinPosition,
			float progress) {
			return new(
				correlationId,
				new ResolvedEvent(
					positionStreamId, positionSequenceNumber, eventStreamId, eventSequenceNumber, resolvedLinkTo,
					position, originalPosition, eventId, eventType, isJson, data, metadata, null, null,
					default),
				safeTransactionFileReaderJoinPosition, progress);
		}

		public static CommittedEventDistributed Sample(
			Guid correlationId, TFPos position, string eventStreamId, long eventSequenceNumber,
			bool resolvedLinkTo, Guid eventId, string eventType, bool isJson, byte[] data, byte[] metadata,
			DateTime? timestamp = null) {
			return new(
				correlationId,
				new ResolvedEvent(
					eventStreamId, eventSequenceNumber, eventStreamId, eventSequenceNumber, resolvedLinkTo,
					position,
					position, eventId, eventType, isJson, data, metadata, null, null,
					timestamp.GetValueOrDefault()),
				position.PreparePosition, 11.1f);
		}

		//NOTE: committed event with null event _data means - end of the source reached.
		// Current last available TF commit position is in _position.CommitPosition
		// TODO: separate message?

		public CommittedEventDistributed(Guid correlationId, ResolvedEvent data)
			: this(correlationId, data, data.Position.PreparePosition, 11.1f) {
		}

		public ResolvedEvent Data { get; } = data;
		public long? SafeTransactionFileReaderJoinPosition { get; } = safeTransactionFileReaderJoinPosition;
		public float Progress { get; } = progress;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class Faulted(Guid correlationId, string reason, object source = null) : SubscriptionMessage(correlationId, source) {
		public string Reason { get; } = reason;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class ReportProgress(Guid correlationId, object source = null) : SubscriptionMessage(correlationId, source);
}
