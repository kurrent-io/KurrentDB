// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using EventStore.LogCommon;
using Xunit;

namespace EventStore.LogV3.Tests;

public class RecordCreatorTests {
	readonly Guid _guid1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
	readonly Guid _guid2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
	readonly Guid _guid3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
	readonly DateTime _dateTime1 = new(0x08_D9_15_80_C3_A1_00_00);
	readonly int _int1 = 1;
	readonly long _long1 = 1;
	readonly long _long2 = 2;
	readonly long _long3 = 3;
	readonly long _long4 = 4;
	readonly uint _uint1 = 5;
	readonly ushort _ushort1 = 6;
	readonly string _string1 = "one";
	readonly Raw.EventFlags _prepareflags = (Raw.EventFlags)3;
	readonly Raw.PartitionFlags _partitionFlags = (Raw.PartitionFlags)4;
	readonly ReadOnlyMemory<byte> _bytes1 = new byte[] { 0x10 };
	readonly ReadOnlyMemory<byte> _bytes2 = new byte[] { 0x20, 0x01 };
	readonly ReadOnlyMemory<byte> _bytes3 = new byte[] { 0x30, 0x01, 0x01 };
	readonly ReadOnlyMemory<byte> _bytes4 = new byte[] { 0x40, 0x01, 0x01, 0x01 };

	[Fact]
	public void can_create_epoch() {
		var epochNumber = 5;
		var record = RecordCreator.CreateEpochRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			epochNumber: epochNumber,
			epochId: _guid1,
			prevEpochPosition: _long2,
			leaderInstanceId: _guid2);

		Assert.Equal(LogRecordType.System, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV1, record.Header.Version);

		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);

		Assert.Equal(epochNumber, record.SubHeader.EpochNumber);
		Assert.Equal(_guid2, record.SubHeader.LeaderInstanceId);
		Assert.Equal(_long2, record.SubHeader.PrevEpochPosition);
	}
	
	[Fact]
	public void can_create_partition_record() {
		var record = RecordCreator.CreatePartitionRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			partitionId: _guid1,
			partitionTypeId: _guid2,
			parentPartitionId: _guid3,
			flags: _partitionFlags,
			referenceNumber: _ushort1,
			name: _string1);

		Assert.Equal(LogRecordType.Partition, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(_guid2, record.SubHeader.PartitionTypeId);
		Assert.Equal(_guid3, record.SubHeader.ParentPartitionId);
		Assert.Equal(_partitionFlags, record.SubHeader.Flags);
		Assert.Equal(_ushort1, record.SubHeader.ReferenceNumber);
		Assert.Equal(_string1, record.StringPayload);
	}
	
	[Fact]
	public void can_create_partition_type_record() {
		var record = RecordCreator.CreatePartitionTypeRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			partitionTypeId: _guid1,
			partitionId: _guid2,
			name: _string1);

		Assert.Equal(LogRecordType.PartitionType, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(_guid2, record.SubHeader.PartitionId);
		Assert.Equal(_string1, record.StringPayload);
	}

	[Fact]
	public void can_create_stream_record() {
		var record = RecordCreator.CreateStreamRecord(
			streamId: _guid1,
			timeStamp: _dateTime1,
			logPosition: _long1,
			streamNumber: _uint1,
			streamName: _string1,
			partitionId: _guid2,
			streamTypeId: _guid3);

		Assert.Equal(LogRecordType.Stream, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(_guid2, record.SubHeader.PartitionId);
		Assert.Equal(_uint1, record.SubHeader.ReferenceNumber);
		Assert.Equal(_guid3, record.SubHeader.StreamTypeId);
		Assert.Equal(_string1, record.StringPayload);
	}

	[Fact]
	public void can_create_stream_type_record() {
		var record = RecordCreator.CreateStreamTypeRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			streamTypeId: _guid1,
			partitionId: _guid2,
			name: _string1);

		Assert.Equal(LogRecordType.StreamType, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(_guid2, record.SubHeader.PartitionId);
		Assert.Equal(_string1, record.StringPayload);
	}
	
	[Fact]
	public void can_create_event_type_record() {
		var record = RecordCreator.CreateEventTypeRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			eventTypeId: _guid1,
			parentEventTypeId: _guid2,
			partitionId: _guid3,
			eventTypeNumber: _uint1,
			eventTypeVersion: _ushort1,
			name: _string1);

		Assert.Equal(LogRecordType.EventType, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(_guid2, record.SubHeader.ParentEventTypeId);
		Assert.Equal(_guid3, record.SubHeader.PartitionId);
		Assert.Equal(_uint1, record.SubHeader.ReferenceNumber);
		Assert.Equal(_ushort1, record.SubHeader.Version);
		Assert.Equal(_string1, record.StringPayload);
	}
	
	[Fact]
	public void can_create_content_type_record() {
		var record = RecordCreator.CreateContentTypeRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			contentTypeId: _guid1,
			partitionId: _guid2,
			referenceNumber: _ushort1,
			name: _string1);

		Assert.Equal(LogRecordType.ContentType, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(_guid2, record.SubHeader.PartitionId);
		Assert.Equal(_ushort1, record.SubHeader.ReferenceNumber);
		Assert.Equal(_string1, record.StringPayload);
	}

	[Fact]
	public void can_create_stream_write_record_for_single_event() {
		var record = RecordCreator.CreateStreamWriteRecordForSingleEvent(
			timeStamp: _dateTime1,
			correlationId: _guid1,
			logPosition: _long1,
			transactionPosition: _long2,
			transactionOffset: _int1,
			streamNumber: _long3,
			startingEventNumber: _long4,
			eventId: _guid2,
			eventTypeNumber: _uint1,
			eventData: _bytes3.Span,
			eventMetadata: _bytes4.Span,
			eventFlags: _prepareflags);

		Assert.Equal(LogRecordType.StreamWrite, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal<Guid>(_guid1, record.SystemMetadata.CorrelationId);
		Assert.Equal(_long2, record.SystemMetadata.TransactionPosition);
		Assert.Equal(_int1, record.SystemMetadata.TransactionOffset);
		Assert.Equal(0, record.SystemMetadata.StartingEventNumberRoot);
		Assert.Equal(0, record.SystemMetadata.StartingEventNumberCategory);
		Assert.Equal(0, record.WriteId.CategoryNumber);
		Assert.Equal(0, record.WriteId.ParentTopicNumber);
		Assert.Equal(0, record.WriteId.TopicNumber);
		Assert.Equal(_long3, record.WriteId.StreamNumber);
		Assert.Equal(_long4, record.WriteId.StartingEventNumber);
		Assert.Equal<Guid>(_guid2, record.Event.SystemMetadata.EventId);
		Assert.Equal(_uint1, record.Event.Header.EventTypeNumber);
		Assert.Equal(MemoryMarshal.ToEnumerable(_bytes3), MemoryMarshal.ToEnumerable(record.Event.Data));
		Assert.Equal(MemoryMarshal.ToEnumerable(_bytes4), MemoryMarshal.ToEnumerable(record.Event.Metadata));
		Assert.Equal(_prepareflags, record.Event.Header.Flags);
	}
	
	[Fact]
	public void can_create_transaction_start() {
		var record = RecordCreator.CreateTransactionStartRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			transactionId: _guid1,
			status: Raw.TransactionStatus.Failed,
			type: Raw.TransactionType.MultipartTransaction,
			recordCount: _uint1);

		Assert.Equal(LogRecordType.TransactionStart, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(Raw.TransactionStatus.Failed, record.SubHeader.Status);
		Assert.Equal(Raw.TransactionType.MultipartTransaction, record.SubHeader.Type);
		Assert.Equal(_uint1, record.SubHeader.RecordCount);
	}
	
	[Fact]
	public void can_create_transaction_end() {
		var record = RecordCreator.CreateTransactionEndRecord(
			timeStamp: _dateTime1,
			logPosition: _long1,
			transactionId: _guid1,
			recordCount: _uint1);

		Assert.Equal(LogRecordType.TransactionEnd, record.Header.Type);
		Assert.Equal(LogRecordVersion.LogRecordV0, record.Header.Version);
		Assert.Equal(_dateTime1, record.Header.TimeStamp);
		Assert.Equal(_guid1, record.Header.RecordId);
		Assert.Equal(_long1, record.Header.LogPosition);
		Assert.Equal(_uint1, record.SubHeader.RecordCount);
	}
}
