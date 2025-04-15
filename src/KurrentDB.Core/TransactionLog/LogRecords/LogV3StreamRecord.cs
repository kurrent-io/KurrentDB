// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.LogV3;
using KurrentDB.Core.Data;
using KurrentDB.LogV3;
using StreamId = System.UInt32;

namespace KurrentDB.Core.TransactionLog.LogRecords;

// todo: when we have partition records etc there might be a baseclass to refactor
// the string payload to.
public class LogV3StreamRecord : LogV3Record<StringPayloadRecord<Raw.StreamHeader>>, IEquatable<LogV3StreamRecord>, IPrepareLogRecord<StreamId> {
	public StreamId EventStreamId => LogV3SystemStreams.StreamsCreatedStreamNumber;
	// so we can see the stream name in the webui if we want
	public PrepareFlags Flags => PrepareFlags.SingleWrite | PrepareFlags.IsCommitted | PrepareFlags.IsJson;
	public long TransactionPosition => LogPosition;
	public int TransactionOffset => 0;
	public long ExpectedVersion => StreamIdConverter.ToEventNumber(Record.SubHeader.ReferenceNumber) - 1;
	public Guid EventId => Record.Header.RecordId;
	public Guid CorrelationId { get; } = Guid.NewGuid();
	public uint EventType => LogV3SystemEventTypes.StreamCreatedNumber;
	// so we can see the stream name in the webui if we want
	public ReadOnlyMemory<byte> Data => Record.Payload;
	public ReadOnlyMemory<byte> Metadata => ReadOnlyMemory<byte>.Empty;
	public SchemaInfo DataSchemaInfo => SchemaInfo.None;
	public SchemaInfo MetadataSchemaInfo => SchemaInfo.None;

	public string StreamName => Record.StringPayload;
	public StreamId StreamNumber => Record.SubHeader.ReferenceNumber;

	public LogV3StreamRecord(
		Guid streamId,
		long logPosition,
		DateTime timeStamp,
		uint streamNumber,
		string streamName,
		Guid partitionId) : base() {

		Record = RecordCreator.CreateStreamRecord(
			streamId: streamId,
			timeStamp: timeStamp,
			logPosition: logPosition,
			streamNumber: streamNumber,
			streamName: streamName,
			partitionId: partitionId,
			streamTypeId: Guid.Empty);
	}

	public LogV3StreamRecord(ReadOnlyMemory<byte> bytes) : base() {
		Record = StringPayloadRecord.Create(new RecordView<Raw.StreamHeader>(bytes));
	}

	public IPrepareLogRecord<StreamId> CopyForRetry(long logPosition, long transactionPosition) {
		return new LogV3StreamRecord(
			streamId: Record.Header.RecordId,
			timeStamp: Record.Header.TimeStamp,
			logPosition: logPosition,
			streamNumber: Record.SubHeader.ReferenceNumber,
			streamName: Record.StringPayload,
			partitionId: Record.SubHeader.PartitionId);
	}

	public bool Equals(LogV3StreamRecord other) {
		if (other is null)
			return false;
		if (ReferenceEquals(this, other))
			return true;
		return
			other.StreamName == StreamName &&
			other.StreamNumber == StreamNumber &&
			other.Record.Bytes.Span.SequenceEqual(Record.Bytes.Span);
	}
}
