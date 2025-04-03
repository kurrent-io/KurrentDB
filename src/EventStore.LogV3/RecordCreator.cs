// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Text;
using System.Text.Unicode;
using EventStore.LogCommon;
using Google.Protobuf;

namespace EventStore.LogV3;

// Nice-to-use wrappers for creating and populating the raw structures.
public static class RecordCreator {
	// these exist to make the code more obviously correct
	private static byte CurrentVersion(this ref Raw.EpochHeader _) => LogRecordVersion.LogRecordV1;
	private static byte CurrentVersion(this ref Raw.PartitionHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.PartitionTypeHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.StreamHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.StreamTypeHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.StreamWriteHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.EventTypeHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.ContentTypeHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.TransactionStartHeader _) => LogRecordVersion.LogRecordV0;
	private static byte CurrentVersion(this ref Raw.TransactionEndHeader _) => LogRecordVersion.LogRecordV0;

	private static LogRecordType Type(this ref Raw.EpochHeader _) => LogRecordType.System;
	private static LogRecordType Type(this ref Raw.PartitionHeader _) => LogRecordType.Partition;
	private static LogRecordType Type(this ref Raw.PartitionTypeHeader _) => LogRecordType.PartitionType;
	private static LogRecordType Type(this ref Raw.StreamHeader _) => LogRecordType.Stream;
	private static LogRecordType Type(this ref Raw.StreamTypeHeader _) => LogRecordType.StreamType;
	private static LogRecordType Type(this ref Raw.StreamWriteHeader _) => LogRecordType.StreamWrite;
	private static LogRecordType Type(this ref Raw.EventTypeHeader _) => LogRecordType.EventType;
	private static LogRecordType Type(this ref Raw.ContentTypeHeader _) => LogRecordType.ContentType;
	private static LogRecordType Type(this ref Raw.TransactionStartHeader _) => LogRecordType.TransactionStart;
	private static LogRecordType Type(this ref Raw.TransactionEndHeader _) => LogRecordType.TransactionEnd;
	
	// todo: limit of 100 bytes when we know a good way to report the error to the client.
	// throwing here will crash the writer.
	const int MaxStringBytes = int.MaxValue;
	public static void PopulateString(string str, Span<byte> target) {
		Utf8.FromUtf16(str, target, out _, out var bytesWritten, true, true);
		if (bytesWritten > MaxStringBytes)
			throw new ArgumentException($"Name \"{str}\" is longer than {MaxStringBytes} bytes: {bytesWritten}");
	}

	private static readonly Encoding _utf8NoBom = new UTF8Encoding(
		encoderShouldEmitUTF8Identifier: false,
		throwOnInvalidBytes: true);

	public static RecordView<Raw.EpochHeader> CreateEpochRecord(
		DateTime timeStamp,
		long logPosition,
		int epochNumber,
		Guid epochId,
		long prevEpochPosition,
		Guid leaderInstanceId) {

		var record = MutableRecordView<Raw.EpochHeader>.Create(payloadLength: 0);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = epochId;
		header.LogPosition = logPosition;

		subHeader.EpochNumber = epochNumber;
		subHeader.LeaderInstanceId = leaderInstanceId;
		subHeader.PrevEpochPosition = prevEpochPosition;

		return record;
	}

	public static StringPayloadRecord<Raw.PartitionHeader> CreatePartitionRecord(
		DateTime timeStamp,
		long logPosition,
		Guid partitionId,
		Guid partitionTypeId,
		Guid parentPartitionId,
		Raw.PartitionFlags flags,
		ushort referenceNumber,
		string name) {

		var payloadLength = _utf8NoBom.GetByteCount(name);
		var record = MutableRecordView<Raw.PartitionHeader>.Create(payloadLength);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = partitionId;
		header.LogPosition = logPosition;

		subHeader.PartitionTypeId = partitionTypeId;
		subHeader.ParentPartitionId = parentPartitionId;
		subHeader.Flags = flags;
		subHeader.ReferenceNumber = referenceNumber;
		PopulateString(name, record.Payload.Span);

		return StringPayloadRecord.Create(record);
	}
	
	public static StringPayloadRecord<Raw.PartitionTypeHeader> CreatePartitionTypeRecord(
		DateTime timeStamp,
		long logPosition,
		Guid partitionTypeId,
		Guid partitionId,
		string name) {

		var payloadLength = _utf8NoBom.GetByteCount(name);
		var record = MutableRecordView<Raw.PartitionTypeHeader>.Create(payloadLength);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = partitionTypeId;
		header.LogPosition = logPosition;

		subHeader.PartitionId = partitionId;
		PopulateString(name, record.Payload.Span);

		return StringPayloadRecord.Create(record);
	}

	public static StringPayloadRecord<Raw.StreamHeader> CreateStreamRecord(
		Guid streamId,
		DateTime timeStamp,
		long logPosition,
		uint streamNumber,
		string streamName,
		Guid partitionId,
		Guid streamTypeId) {

		var payloadLength = _utf8NoBom.GetByteCount(streamName);
		var record = MutableRecordView<Raw.StreamHeader>.Create(payloadLength);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.RecordId = streamId;
		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.LogPosition = logPosition;

		subHeader.PartitionId = partitionId;
		subHeader.StreamTypeId = streamTypeId;
		subHeader.ReferenceNumber = streamNumber;

		PopulateString(streamName, record.Payload.Span);

		return StringPayloadRecord.Create(record);
	}

	public static StringPayloadRecord<Raw.StreamTypeHeader> CreateStreamTypeRecord(
		DateTime timeStamp,
		long logPosition,
		Guid streamTypeId,
		Guid partitionId,
		string name) {

		var payloadLength = _utf8NoBom.GetByteCount(name);
		var record = MutableRecordView<Raw.StreamTypeHeader>.Create(payloadLength);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = streamTypeId;
		header.LogPosition = logPosition;

		subHeader.PartitionId = partitionId;
		PopulateString(name, record.Payload.Span);

		return StringPayloadRecord.Create(record);
	}
	
	public static StringPayloadRecord<Raw.EventTypeHeader> CreateEventTypeRecord(
		DateTime timeStamp,
		long logPosition,
		Guid eventTypeId,
		Guid parentEventTypeId,
		Guid partitionId,
		uint eventTypeNumber,
		ushort eventTypeVersion,
		string name) {

		var payloadLength = _utf8NoBom.GetByteCount(name);
		var record = MutableRecordView<Raw.EventTypeHeader>.Create(payloadLength);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = eventTypeId;
		header.LogPosition = logPosition;

		subHeader.ParentEventTypeId = parentEventTypeId;
		subHeader.PartitionId = partitionId;
		subHeader.ReferenceNumber = eventTypeNumber;
		subHeader.Version = eventTypeVersion;
		PopulateString(name, record.Payload.Span);

		return StringPayloadRecord.Create(record);
	}
	
	public static StringPayloadRecord<Raw.ContentTypeHeader> CreateContentTypeRecord(
		DateTime timeStamp,
		long logPosition,
		Guid contentTypeId,
		Guid partitionId,
		ushort referenceNumber,
		string name) {

		var payloadLength = _utf8NoBom.GetByteCount(name);
		var record = MutableRecordView<Raw.ContentTypeHeader>.Create(payloadLength);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = contentTypeId;
		header.LogPosition = logPosition;

		subHeader.PartitionId = partitionId;
		subHeader.ReferenceNumber = referenceNumber;
		PopulateString(name, record.Payload.Span);

		return StringPayloadRecord.Create(record);
	}

	public static StreamWriteRecord CreateStreamWriteRecordForSingleEvent(
		DateTime timeStamp,
		Guid correlationId,
		long logPosition,
		long transactionPosition,
		int transactionOffset,
		long streamNumber,
		long startingEventNumber,
		Guid eventId,
		uint eventTypeNumber,
		ReadOnlySpan<byte> eventData,
		ReadOnlySpan<byte> eventMetadata,
		Raw.EventFlags eventFlags) {

		var writeSystemMetadata = new StreamWriteSystemMetadata {
			CorrelationId = correlationId,
			TransactionPosition = transactionPosition,
			TransactionOffset = transactionOffset,
			StartingEventNumberRoot = 0,
			StartingEventNumberCategory = 0,
		};

		var eventSystemMetadata = new EventSystemMetadata {
			EventId = eventId,
		};

		var writeSystemMetadataSize = writeSystemMetadata.CalculateSize();
		var eventSystemMetadataSize = eventSystemMetadata.CalculateSize();

		var payloadLength = MeasureWritePayload(
			recordMetadataSize: writeSystemMetadataSize,
			eventSystemMetadataSize: eventSystemMetadataSize,
			userData: eventData,
			userMetadata: eventMetadata);

		var record = MutableRecordView<Raw.StreamWriteHeader>.Create(payloadLength);
		ref var header = ref record.Header;
		ref var recordId = ref record.RecordId<Raw.StreamWriteId>();
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.LogPosition = logPosition;

		recordId.ParentTopicNumber = 0;
		recordId.TopicNumber = 0;
		recordId.CategoryNumber = 0;
		recordId.StreamNumber = (uint)streamNumber; // todo: remove cast
		recordId.StartingEventNumber = startingEventNumber;

		subHeader.Count = 1;
		subHeader.Flags = 0;
		subHeader.MetadataSize = writeSystemMetadataSize;

		var slicer = record.Payload.Slicer();
		writeSystemMetadata.WriteTo(slicer.Slice(subHeader.MetadataSize).Span);

		PopulateEventSubRecord(
			eventTypeNumber: eventTypeNumber,
			flags: eventFlags,
			systemMetadataSize: eventSystemMetadataSize,
			systemMetadata: eventSystemMetadata,
			data: eventData,
			metadata: eventMetadata,
			target: slicer.Remaining);

		return new StreamWriteRecord(record);

		static int MeasureWritePayload(
			int recordMetadataSize,
			int eventSystemMetadataSize,
			ReadOnlySpan<byte> userData,
			ReadOnlySpan<byte> userMetadata) {

			var result =
				recordMetadataSize +
				MeasureForEventSubRecord(
					systemMetadataSize: eventSystemMetadataSize,
					data: userData,
					metadata: userMetadata);
			return result;
		}

		static int MeasureForEventSubRecord(
			int systemMetadataSize,
			ReadOnlySpan<byte> data,
			ReadOnlySpan<byte> metadata) {

			return
				Raw.EventHeader.Size +
				systemMetadataSize +
				data.Length +
				metadata.Length;
		}

		static void PopulateEventSubRecord(
			uint eventTypeNumber,
			Raw.EventFlags flags,
			int systemMetadataSize,
			EventSystemMetadata systemMetadata,
			ReadOnlySpan<byte> data,
			ReadOnlySpan<byte> metadata,
			Memory<byte> target) {

			var slicer = target.Slicer();
			ref var header = ref slicer.SliceAs<Raw.EventHeader>();
			header.EventTypeNumber = eventTypeNumber;
			header.Flags = flags;
			header.EventSize = MeasureForEventSubRecord(systemMetadataSize, data, metadata);
			header.SystemMetadataSize = systemMetadataSize;
			header.DataSize = data.Length;

			systemMetadata.WriteTo(slicer.Slice(systemMetadataSize).Span);
			data.CopyTo(slicer.Slice(header.DataSize).Span);
			metadata.CopyTo(slicer.Remaining.Span);
		}
	}

	public static RecordView<Raw.TransactionStartHeader> CreateTransactionStartRecord(
		DateTime timeStamp,
		long logPosition,
		Guid transactionId,
		Raw.TransactionStatus status,
		Raw.TransactionType type,
		uint recordCount) {

		var record = MutableRecordView<Raw.TransactionStartHeader>.Create(payloadLength: 0);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;
		
		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = transactionId;
		header.LogPosition = logPosition;

		subHeader.Status = status;
		subHeader.Type = type;
		subHeader.RecordCount = recordCount;

		return record;
	}
	
	public static RecordView<Raw.TransactionEndHeader> CreateTransactionEndRecord(
		DateTime timeStamp,
		long logPosition,
		Guid transactionId,
		uint recordCount) {

		var record = MutableRecordView<Raw.TransactionEndHeader>.Create(payloadLength: 0);
		ref var header = ref record.Header;
		ref var subHeader = ref record.SubHeader;

		header.Type = subHeader.Type();
		header.Version = subHeader.CurrentVersion();
		header.TimeStamp = timeStamp;
		header.RecordId = transactionId;
		header.LogPosition = logPosition;
		
		subHeader.RecordCount = recordCount;

		return record;
	}
}
