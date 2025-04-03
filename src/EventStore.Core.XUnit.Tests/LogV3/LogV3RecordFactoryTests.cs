// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using EventStore.Core.LogV3;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogV3;

// we dont want too many tests here, rather pass this factory into the higher up
// integration tests like we do for the v2 factory.
public class LogV3RecordFactoryTests {
	readonly Guid _guid1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
	readonly Guid _guid2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
	readonly DateTime _dateTime1 = new(0x08_D9_15_80_C3_A1_00_00);
	readonly long _long1 = 1;
	readonly long _long2 = 2;
	readonly long _long4 = 4;
	readonly uint _uint3000 = 3000;
	readonly ushort _ushort = 5;
	readonly string _string1 = "string-one";
	readonly LogV3RecordFactory _sut = new();
	readonly PrepareFlags _prepareflags = PrepareFlags.SingleWrite;
	readonly ReadOnlyMemory<byte> _bytes1 = new byte[] { 0x10 }; 
	readonly ReadOnlyMemory<byte> _bytes2 = new byte[] { 0x20, 0x01 };

	[Fact]
	public void can_create_epoch() {
		var epochNumber = 5;
		var epochIn = new EpochRecord(
			epochPosition: _long1,
			epochNumber: epochNumber,
			epochId: _guid1,
			prevEpochPosition: _long2,
			timeStamp: _dateTime1,
			leaderInstanceId: _guid2);

		var systemEpoch = _sut.CreateEpoch(epochIn);

		var epochOut = systemEpoch.GetEpochRecord();

		Assert.Equal(_long1, systemEpoch.LogPosition);
		Assert.Equal(LogRecordType.System, systemEpoch.RecordType);
		Assert.Equal(SystemRecordType.Epoch, systemEpoch.SystemRecordType);
		Assert.Equal(_long1, systemEpoch.Version);

		Assert.Equal(epochIn.EpochPosition, epochOut.EpochPosition);
		Assert.Equal(epochIn.TimeStamp, epochOut.TimeStamp);
		Assert.Equal(epochIn.EpochId, epochOut.EpochId);
		Assert.Equal(epochIn.EpochNumber, epochOut.EpochNumber);
		Assert.Equal(epochIn.PrevEpochPosition, epochOut.PrevEpochPosition);
		Assert.Equal(epochIn.LeaderInstanceId, epochOut.LeaderInstanceId);
	}

	[Fact]
	public void can_create_prepare() {
		var prepare = _sut.CreatePrepare(
			logPosition: _long1,
			correlationId: _guid1,
			eventId: _guid2,
			// must be same as logPosition
			transactionPosition: _long1,
			// must be 0 since only one event is supported at the moment
			transactionOffset: 0,
			eventStreamId: _uint3000,
			expectedVersion: _long4,
			timeStamp: _dateTime1,
			flags: _prepareflags,
			eventType: _uint3000,
			data: _bytes1,
			metadata: _bytes2);

		Assert.Equal(_long1, prepare.LogPosition);
		Assert.Equal(_guid1, prepare.CorrelationId);
		Assert.Equal(_guid2, prepare.EventId);
		Assert.Equal(_long1, prepare.TransactionPosition);
		Assert.Equal(0, prepare.TransactionOffset);
		Assert.Equal(_uint3000, prepare.EventStreamId);
		Assert.Equal(_long4, prepare.ExpectedVersion);
		Assert.Equal(_dateTime1, prepare.TimeStamp);
		Assert.Equal(_prepareflags, prepare.Flags);
		Assert.Equal(_uint3000, prepare.EventType);
		Assert.Equal(MemoryMarshal.ToEnumerable(_bytes1), MemoryMarshal.ToEnumerable(prepare.Data));
		Assert.Equal(MemoryMarshal.ToEnumerable(_bytes2), MemoryMarshal.ToEnumerable(prepare.Metadata));
	}

	[Fact]
	public void can_create_event_type() {
		var eventType = (LogV3EventTypeRecord)_sut.CreateEventTypeRecord(
			eventTypeId: _guid1,
			parentEventTypeId: _guid2,
			eventType: _string1,
			eventTypeNumber: _uint3000,
			eventTypeVersion: _ushort,
			logPosition: _long1,
			timeStamp: _dateTime1);
		
		Assert.Equal(_guid1, eventType.EventId);
		Assert.Equal(_guid2, eventType.ParentEventId);
		Assert.Equal(_string1, eventType.EventTypeName);
		Assert.Equal(_uint3000, eventType.EventTypeNumber);
		Assert.Equal(_ushort, eventType.EventTypeVersion);
		Assert.Equal(_long1, eventType.LogPosition);
		Assert.Equal(_dateTime1, eventType.TimeStamp);
	}
}
