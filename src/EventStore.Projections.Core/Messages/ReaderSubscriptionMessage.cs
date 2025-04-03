// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Messages;

public static partial class ReaderSubscriptionMessage {
	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class SubscriptionMessage : Message {
		private readonly Guid _correlationId;
		private readonly CheckpointTag _preTagged;
		private readonly object _source;

		public SubscriptionMessage(Guid correlationId, CheckpointTag preTagged, object source) {
			_correlationId = correlationId;
			_preTagged = preTagged;
			_source = source;
		}

		public Guid CorrelationId {
			get { return _correlationId; }
		}

		public CheckpointTag PreTagged {
			get { return _preTagged; }
		}

		public object Source {
			get { return _source; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class EventReaderIdle : SubscriptionMessage {
		private readonly DateTime _idleTimestampUtc;

		public EventReaderIdle(Guid correlationId, DateTime idleTimestampUtc, object source = null)
			: base(correlationId, null, source) {
			_idleTimestampUtc = idleTimestampUtc;
		}

		public DateTime IdleTimestampUtc {
			get { return _idleTimestampUtc; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public sealed partial class EventReaderStarting : SubscriptionMessage {
		private readonly long _lastCommitPosition;

		public EventReaderStarting(Guid correlationId, long lastCommitPosition, object source = null)
			: base(correlationId, null, source) {
			_lastCommitPosition = lastCommitPosition;
		}

		public long LastCommitPosition {
			get { return _lastCommitPosition; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class EventReaderEof : SubscriptionMessage {
		private readonly bool _maxEventsReached;

		public EventReaderEof(Guid correlationId, bool maxEventsReached = false, object source = null)
			: base(correlationId, null, source) {
			_maxEventsReached = maxEventsReached;
		}

		public bool MaxEventsReached {
			get { return _maxEventsReached; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class EventReaderPartitionEof : SubscriptionMessage {
		private readonly string _partition;

		public EventReaderPartitionEof(
			Guid correlationId, string partition, CheckpointTag preTagged, object source = null)
			: base(correlationId, preTagged, source) {
			_partition = partition;
		}

		public string Partition {
			get { return _partition; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class EventReaderPartitionDeleted : SubscriptionMessage {
		private readonly string _partition;
		private readonly long? _lastEventNumber;
		private readonly TFPos? _deleteLinkOrEventPosition;
		private readonly TFPos? _deleteEventOrLinkTargetPosition;
		private readonly string _positionStreamId;
		private readonly long? _positionEventNumber;

		public EventReaderPartitionDeleted(
			Guid correlationId, string partition, long? lastEventNumber, TFPos? deleteLinkOrEventPosition,
			TFPos? deleteEventOrLinkTargetPosition, string positionStreamId, long? positionEventNumber,
			CheckpointTag preTagged = null, object source = null)
			: base(correlationId, preTagged, source) {
			_partition = partition;
			_lastEventNumber = lastEventNumber;
			_deleteLinkOrEventPosition = deleteLinkOrEventPosition;
			_deleteEventOrLinkTargetPosition = deleteEventOrLinkTargetPosition;
			_positionStreamId = positionStreamId;
			_positionEventNumber = positionEventNumber;
		}

		public string Partition {
			get { return _partition; }
		}

		public long? LastEventNumber {
			get { return _lastEventNumber; }
		}

		public TFPos? DeleteEventOrLinkTargetPosition {
			get { return _deleteEventOrLinkTargetPosition; }
		}

		public string PositionStreamId {
			get { return _positionStreamId; }
		}

		public long? PositionEventNumber {
			get { return _positionEventNumber; }
		}

		public TFPos? DeleteLinkOrEventPosition {
			get { return _deleteLinkOrEventPosition; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public sealed partial class EventReaderNotAuthorized : SubscriptionMessage {
		public EventReaderNotAuthorized(Guid correlationId, object source = null)
			: base(correlationId, null, source) {
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class CommittedEventDistributed : SubscriptionMessage {
		public static CommittedEventDistributed Sample(
			Guid correlationId, TFPos position, TFPos originalPosition, string positionStreamId,
			long positionSequenceNumber,
			string eventStreamId, long eventSequenceNumber, bool resolvedLinkTo, Guid eventId, string eventType,
			bool isJson, byte[] data, byte[] metadata, long? safeTransactionFileReaderJoinPosition,
			float progress) {
			return new CommittedEventDistributed(
				correlationId,
				new ResolvedEvent(
					positionStreamId, positionSequenceNumber, eventStreamId, eventSequenceNumber, resolvedLinkTo,
					position, originalPosition, eventId, eventType, isJson, data, metadata, null, null,
					default(DateTime)),
				safeTransactionFileReaderJoinPosition, progress);
		}

		public static CommittedEventDistributed Sample(
			Guid correlationId, TFPos position, string eventStreamId, long eventSequenceNumber,
			bool resolvedLinkTo, Guid eventId, string eventType, bool isJson, byte[] data, byte[] metadata,
			DateTime? timestamp = null) {
			return new CommittedEventDistributed(
				correlationId,
				new ResolvedEvent(
					eventStreamId, eventSequenceNumber, eventStreamId, eventSequenceNumber, resolvedLinkTo,
					position,
					position, eventId, eventType, isJson, data, metadata, null, null,
					timestamp.GetValueOrDefault()),
				position.PreparePosition, 11.1f);
		}

		private readonly ResolvedEvent _data;
		private readonly long? _safeTransactionFileReaderJoinPosition;
		private readonly float _progress;

		//NOTE: committed event with null event _data means - end of the source reached.
		// Current last available TF commit position is in _position.CommitPosition
		// TODO: separate message?

		public CommittedEventDistributed(
			Guid correlationId, ResolvedEvent data, long? safeTransactionFileReaderJoinPosition, float progress,
			object source = null, CheckpointTag preTagged = null)
			: base(correlationId, preTagged, source) {
			_data = data;
			_safeTransactionFileReaderJoinPosition = safeTransactionFileReaderJoinPosition;
			_progress = progress;
		}

		public CommittedEventDistributed(Guid correlationId, ResolvedEvent data, CheckpointTag preTagged = null)
			: this(correlationId, data, data.Position.PreparePosition, 11.1f, preTagged) {
		}

		public ResolvedEvent Data {
			get { return _data; }
		}

		public long? SafeTransactionFileReaderJoinPosition {
			get { return _safeTransactionFileReaderJoinPosition; }
		}

		public float Progress {
			get { return _progress; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class Faulted : SubscriptionMessage {
		private readonly string _reason;

		public Faulted(
			Guid correlationId, string reason, object source = null)
			: base(correlationId, null, source) {
			_reason = reason;
		}

		public string Reason {
			get { return _reason; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscription)]
	public partial class ReportProgress : SubscriptionMessage {
		public ReportProgress(Guid correlationId, object source = null) : base(correlationId, null, source) { }
	}
}
