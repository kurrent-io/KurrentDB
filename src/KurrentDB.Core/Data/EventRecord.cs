// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.LogRecords;
using JetBrains.Annotations;
using KurrentDB.Core.Services.Transport.Grpc;

namespace KurrentDB.Core.Data;

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
	public readonly SchemaInfo DataSchemaInfo;
	public readonly SchemaInfo MetadataSchemaInfo;

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
		DataSchemaInfo = prepare.DataSchemaInfo;
		MetadataSchemaInfo = prepare.MetadataSchemaInfo;
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
		byte[] metadata,
		[CanBeNull] SchemaInfo dataSchemaInfo = null,
		[CanBeNull] SchemaInfo metadataSchemaInfo = null) {
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
		DataSchemaInfo = dataSchemaInfo ?? SchemaInfo.None;
		MetadataSchemaInfo = metadataSchemaInfo ?? SchemaInfo.None;
	}

	public Dictionary<string, string> GetMetadataDictionary() {
		var dict = new Dictionary<string, string>();

		dict.Add(Constants.Metadata.Type, EventType);
		dict.Add(Constants.Metadata.Created, TimeStamp.ToTicksSinceEpoch().ToString());

		string contentType;
		if (DataSchemaInfo != SchemaInfo.None) {
			contentType = SchemaInfo.FormatToString(DataSchemaInfo.SchemaFormat);
			dict.Add(Constants.Metadata.SchemaVersionId, DataSchemaInfo.SchemaVersionId.ToString());
		} else {
			contentType = IsJson
				? Constants.Metadata.ContentTypes.ApplicationJson
				: Constants.Metadata.ContentTypes.ApplicationOctetStream;
		}

		string metadataContentType;
		if (MetadataSchemaInfo != SchemaInfo.None) {
			metadataContentType = SchemaInfo.FormatToString(MetadataSchemaInfo.SchemaFormat);
			dict.Add(Constants.Metadata.MetadataSchemaVersionId, MetadataSchemaInfo.SchemaVersionId.ToString());
		} else {
			metadataContentType = IsJson
				? Constants.Metadata.ContentTypes.ApplicationJson
				: Constants.Metadata.ContentTypes.ApplicationOctetStream;
		}

		dict.Add(Constants.Metadata.ContentType, contentType);
		dict.Add(Constants.Metadata.MetadataContentType, metadataContentType);

		return dict;
	}

	public bool Equals(EventRecord other) {
		if (ReferenceEquals(null, other))
			return false;
		if (ReferenceEquals(this, other))
			return true;
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
			   && Metadata.Span.SequenceEqual(other.Metadata.Span)
			   && DataSchemaInfo.Equals(other.DataSchemaInfo)
			   && MetadataSchemaInfo.Equals(other.MetadataSchemaInfo);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj))
			return false;
		if (ReferenceEquals(this, obj))
			return true;
		if (obj.GetType() != GetType())
			return false;
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
			hashCode = (hashCode * 397) ^ DataSchemaInfo.GetHashCode();
			hashCode = (hashCode * 397) ^ MetadataSchemaInfo.GetHashCode();
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
