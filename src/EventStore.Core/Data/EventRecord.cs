// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Data;

public class EventRecord : IEquatable<EventRecord> {
	public bool IsJson {
		get { return (Flags & PrepareFlags.IsJson) == PrepareFlags.IsJson; }
	}

	public bool IsSelfCommitted => Flags.HasAnyOf(PrepareFlags.IsCommitted);

	public readonly long EventNumber;
	public readonly long LogPosition;
	public readonly Guid CorrelationId;
	public readonly Guid EventId;
	public readonly long TransactionPosition;
	public readonly int TransactionOffset;
	public readonly string EventStreamId;
	public readonly long ExpectedVersion;
	public readonly DateTime TimeStamp;
	public readonly PrepareFlags Flags;
	public readonly string EventType;
	public readonly ReadOnlyMemory<byte> Data;
	public readonly ReadOnlyMemory<byte> Metadata;

	public EventRecord(long eventNumber, IPrepareLogRecord prepare, string eventStreamId, string eventType) {
		Ensure.Nonnegative(eventNumber, "eventNumber");
		Ensure.NotNull(eventStreamId, "eventStreamId");

		EventNumber = eventNumber;
		LogPosition = prepare.LogPosition;
		CorrelationId = prepare.CorrelationId;
		EventId = prepare.EventId;
		TransactionPosition = prepare.TransactionPosition;
		TransactionOffset = prepare.TransactionOffset;
		EventStreamId = eventStreamId;
		ExpectedVersion = prepare.ExpectedVersion;
		TimeStamp = prepare.TimeStamp;

		Flags = prepare.Flags;
		EventType = eventType ?? string.Empty;
		Data = prepare.Data;
		Metadata = prepare.Metadata;
	}

	// called from tests only
	public EventRecord(long eventNumber,
		long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		string eventStreamId,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		string eventType,
		byte[] data,
		byte[] metadata) {
		Ensure.Nonnegative(logPosition, "logPosition");
		Ensure.Nonnegative(transactionPosition, "transactionPosition");
		if (transactionOffset < -1)
			throw new ArgumentOutOfRangeException("transactionOffset");
		Ensure.NotNull(eventStreamId, "eventStreamId");
		Ensure.Nonnegative(eventNumber, "eventNumber");
		Ensure.NotEmptyGuid(eventId, "eventId");
		Ensure.NotNull(data, "data");

		EventNumber = eventNumber;
		LogPosition = logPosition;
		CorrelationId = correlationId;
		EventId = eventId;
		TransactionPosition = transactionPosition;
		TransactionOffset = transactionOffset;
		EventStreamId = eventStreamId;
		ExpectedVersion = expectedVersion;
		TimeStamp = timeStamp;
		Flags = flags;
		EventType = eventType ?? string.Empty;
		Data = data ?? Empty.ByteArray;
		Metadata = metadata ?? Empty.ByteArray;
	}

	public bool Equals(EventRecord other) {
		if (ReferenceEquals(null, other)) return false;
		if (ReferenceEquals(this, other)) return true;
		return EventNumber == other.EventNumber
		       && LogPosition == other.LogPosition
		       && CorrelationId.Equals(other.CorrelationId)
		       && EventId.Equals(other.EventId)
		       && TransactionPosition == other.TransactionPosition
		       && TransactionOffset == other.TransactionOffset
		       && string.Equals(EventStreamId, other.EventStreamId)
		       && ExpectedVersion == other.ExpectedVersion
		       && TimeStamp.Equals(other.TimeStamp)
		       && Flags.Equals(other.Flags)
		       && string.Equals(EventType, other.EventType)
		       && Data.Span.SequenceEqual(other.Data.Span)
		       && Metadata.Span.SequenceEqual(other.Metadata.Span);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj)) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != GetType()) return false;
		return Equals((EventRecord)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int hashCode = EventNumber.GetHashCode();
			hashCode = (hashCode * 397) ^ LogPosition.GetHashCode();
			hashCode = (hashCode * 397) ^ CorrelationId.GetHashCode();
			hashCode = (hashCode * 397) ^ EventId.GetHashCode();
			hashCode = (hashCode * 397) ^ TransactionPosition.GetHashCode();
			hashCode = (hashCode * 397) ^ TransactionOffset;
			hashCode = (hashCode * 397) ^ EventStreamId.GetHashCode();
			hashCode = (hashCode * 397) ^ ExpectedVersion.GetHashCode();
			hashCode = (hashCode * 397) ^ TimeStamp.GetHashCode();
			hashCode = (hashCode * 397) ^ Flags.GetHashCode();
			hashCode = (hashCode * 397) ^ EventType.GetHashCode();
			hashCode = (hashCode * 397) ^ Data.GetHashCode();
			hashCode = (hashCode * 397) ^ Metadata.GetHashCode();
			return hashCode;
		}
	}

	public static bool operator ==(EventRecord left, EventRecord right) {
		return Equals(left, right);
	}

	public static bool operator !=(EventRecord left, EventRecord right) {
		return !Equals(left, right);
	}

	public override string ToString() {
		return string.Format("EventNumber: {0}, "
		                     + "LogPosition: {1}, "
		                     + "CorrelationId: {2}, "
		                     + "EventId: {3}, "
		                     + "TransactionPosition: {4}, "
		                     + "TransactionOffset: {5}, "
		                     + "EventStreamId: {6}, "
		                     + "ExpectedVersion: {7}, "
		                     + "TimeStamp: {8}, "
		                     + "Flags: {9}, "
		                     + "EventType: {10}",
			EventNumber,
			LogPosition,
			CorrelationId,
			EventId,
			TransactionPosition,
			TransactionOffset,
			EventStreamId,
			ExpectedVersion,
			TimeStamp,
			Flags,
			EventType);
	}

#if DEBUG
	public string DebugDataView {
		get { return Encoding.UTF8.GetString(Data.Span); }
	}

	public string DebugMetadataView {
		get { return Encoding.UTF8.GetString(Metadata.Span); }
	}
#endif
}
