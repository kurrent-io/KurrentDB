// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Text;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Services.Transport.Grpc.V2.Utils;
using KurrentDB.Core.TransactionLog.LogRecords;
using static KurrentDB.Protobuf.Server.Properties;

namespace KurrentDB.Core.Data;

public class EventRecord : IEquatable<EventRecord> {
	public bool IsJson => (Flags & PrepareFlags.IsJson) == PrepareFlags.IsJson;
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
	public readonly ReadOnlyMemory<byte> Properties;

	// Lazy initialization backing fields
	ReadOnlyMemory<byte> _metadata;
	bool _metadataInitialized;
	object _metadataLock = new();

	public ReadOnlyMemory<byte> Metadata {
		get => LazyInitializer.EnsureInitialized(
			ref _metadata,
			ref _metadataInitialized,
			ref _metadataLock,
			() => Properties.IsEmpty
				? ReadOnlyMemory<byte>.Empty
				: ProtoJsonSerializer.Default.Serialize(
					Parser.ParseFrom(Properties.Span).PropertiesValues.MapToDictionary())
				);
	}

	public EventRecord(long eventNumber, IPrepareLogRecord prepare, string eventStreamId, string? eventType) {
		Ensure.Nonnegative(eventNumber);
		Ensure.NotNull(eventStreamId);

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
		EventType = eventType ?? "";
		Data = prepare.Data;
		Properties = prepare.Properties;

		_metadata = prepare.Metadata;
		_metadataInitialized = !_metadata.IsEmpty;
	}

	// called from tests only
	public EventRecord(
		long eventNumber,
		long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		string eventStreamId,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		string? eventType,
		byte[] data,
		byte[]? metadata,
		byte[]? properties) {
		Ensure.Nonnegative(logPosition);
		Ensure.Nonnegative(transactionPosition);
		ArgumentOutOfRangeException.ThrowIfLessThan(transactionOffset, -1);
		Ensure.NotNull(eventStreamId);
		Ensure.Nonnegative(eventNumber);
		Ensure.NotEmptyGuid(eventId);
		Ensure.NotNull(data);

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
		EventType = eventType ?? "";
		Data = data!;
		Properties = properties ?? [];

		_metadata = metadata ?? [];
		_metadataInitialized = !_metadata.IsEmpty;
	}

	public bool Equals(EventRecord? other) {
		return !ReferenceEquals(null, other) && (ReferenceEquals(this, other) || EventNumber == other.EventNumber
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
			&& Metadata.Span.SequenceEqual(other.Metadata.Span)
			&& Properties.Span.SequenceEqual(other.Properties.Span));
	}

	public override bool Equals(object? obj) =>
		!ReferenceEquals(null, obj) &&
		(ReferenceEquals(this, obj) || obj.GetType() == GetType() && Equals((EventRecord)obj));

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
			hashCode = (hashCode * 397) ^ Properties.GetHashCode();
			return hashCode;
		}
	}

	public static bool operator ==(EventRecord left, EventRecord right) => Equals(left, right);
	public static bool operator !=(EventRecord left, EventRecord right) => !Equals(left, right);

	public override string ToString() {
		return $"EventNumber: {EventNumber}, " +
		       $"LogPosition: {LogPosition}, " +
		       $"CorrelationId: {CorrelationId}, " +
		       $"EventId: {EventId}, " +
		       $"TransactionPosition: {TransactionPosition}, " +
		       $"TransactionOffset: {TransactionOffset}, " +
		       $"EventStreamId: {EventStreamId}, " +
		       $"ExpectedVersion: {ExpectedVersion}, " +
		       $"TimeStamp: {TimeStamp}, " +
		       $"Flags: {Flags}, " +
		       $"EventType: {EventType}";
	}

#if DEBUG
	public string DebugDataView {
		get => Encoding.UTF8.GetString(Data.Span);
	}

	public string DebugMetadataView {
		get => Encoding.UTF8.GetString(Metadata.Span);
	}
#endif
}
