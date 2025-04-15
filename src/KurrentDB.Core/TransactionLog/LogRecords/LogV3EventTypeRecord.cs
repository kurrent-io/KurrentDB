// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.LogV3;
using KurrentDB.Core.Data;
using KurrentDB.LogV3;

namespace KurrentDB.Core.TransactionLog.LogRecords;

public class LogV3EventTypeRecord : LogV3Record<StringPayloadRecord<Raw.EventTypeHeader>>, IEquatable<LogV3EventTypeRecord>, IPrepareLogRecord<uint> {

	public uint EventStreamId => LogV3SystemStreams.EventTypesStreamNumber;
	public PrepareFlags Flags => PrepareFlags.SingleWrite | PrepareFlags.IsCommitted | PrepareFlags.IsJson;
	public long TransactionPosition => LogPosition;
	public int TransactionOffset => 0;
	public long ExpectedVersion => EventTypeIdConverter.ToEventNumber(Record.SubHeader.ReferenceNumber) - 1;
	public Guid EventId => Record.Header.RecordId;
	public Guid ParentEventId => Record.SubHeader.ParentEventTypeId;
	public Guid CorrelationId { get; } = Guid.NewGuid();
	public uint EventType => LogV3SystemEventTypes.EventTypeDefinedNumber;
	// so we can see the event type in the webui if we want
	public ReadOnlyMemory<byte> Data => Record.Payload;
	public ReadOnlyMemory<byte> Metadata => ReadOnlyMemory<byte>.Empty;
	public SchemaInfo DataSchemaInfo => SchemaInfo.None;
	public SchemaInfo MetadataSchemaInfo => SchemaInfo.None;

	public string EventTypeName => Record.StringPayload;
	public uint EventTypeNumber => Record.SubHeader.ReferenceNumber;
	public ushort EventTypeVersion => Record.SubHeader.Version;

	public LogV3EventTypeRecord(
		Guid eventTypeId,
		Guid parentEventTypeId,
		long logPosition,
		DateTime timeStamp,
		string eventType,
		uint eventTypeNumber,
		ushort eventTypeVersion,
		Guid partitionId) : base() {

		Record = RecordCreator.CreateEventTypeRecord(
			eventTypeId: eventTypeId,
			parentEventTypeId: parentEventTypeId,
			timeStamp: timeStamp,
			logPosition: logPosition,
			name: eventType,
			eventTypeNumber: eventTypeNumber,
			eventTypeVersion: eventTypeVersion,
			partitionId: partitionId);
	}

	public LogV3EventTypeRecord(ReadOnlyMemory<byte> bytes) : base() {
		Record = StringPayloadRecord.Create(new RecordView<Raw.EventTypeHeader>(bytes));
	}

	public IPrepareLogRecord<uint> CopyForRetry(long logPosition, long transactionPosition) {
		return new LogV3EventTypeRecord(
			eventTypeId: Record.Header.RecordId,
			timeStamp: Record.Header.TimeStamp,
			logPosition: logPosition,
			eventTypeNumber: Record.SubHeader.ReferenceNumber,
			eventType: Record.StringPayload,
			parentEventTypeId: Record.SubHeader.ParentEventTypeId,
			eventTypeVersion: Record.SubHeader.Version,
			partitionId: Record.SubHeader.PartitionId);
	}

	public bool Equals(LogV3EventTypeRecord other) {
		if (other is null)
			return false;
		if (ReferenceEquals(this, other))
			return true;
		return
			other.EventTypeName == EventTypeName &&
			other.EventTypeNumber == EventTypeNumber &&
			other.Record.Bytes.Span.SequenceEqual(Record.Bytes.Span);
	}
}
