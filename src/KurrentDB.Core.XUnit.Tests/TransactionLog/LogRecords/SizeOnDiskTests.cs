// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using DotNext.Buffers;
using KurrentDB.Core.TransactionLog.LogRecords;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.TransactionLog.LogRecords;

public class SizeOnDiskTests {
	public static TheoryData<ILogRecord> GetLogRecords() {
		return [
			CreatePrepareLogRecord(1),
			CreatePrepareLogRecord(127), // just before needing an extra byte for the 7bit encoding
			CreatePrepareLogRecord(128), // just after
			CreatePrepareLogRecord(100_000),
			..(Enumerable.Range(0, 200).Select(x => CreateV2PrepareLogRecord(
				x % 2 == 0 ? x : 0,
				x % 2 == 1 ? x : 0))),
			CreateCommitLogRecord(),
			CreateSystemLogRecord()
		];

		static PrepareLogRecord CreatePrepareLogRecord(int stringLength) {
			var theString = new string('a', stringLength);

			return new(
				logPosition: 123,
				correlationId: Guid.NewGuid(),
				eventId: Guid.NewGuid(),
				transactionPosition: 456,
				transactionOffset: 321,
				eventStreamId: theString,
				eventStreamIdSize: null,
				expectedVersion: 789,
				timeStamp: DateTime.Now,
				flags: PrepareFlags.SingleWrite,
				eventType: theString,
				eventTypeSize: null,
				data: new byte[] { 0xDE, 0XAD, 0xC0, 0XDE },
				metadata: new byte[] { 0XC0, 0xDE },
				properties: ReadOnlyMemory<byte>.Empty,
				prepareRecordVersion: 1);
		}

		static PrepareLogRecord CreateV2PrepareLogRecord(int metadataBytesLength, int propertiesBytesLength) {
			return new(
				logPosition: 123,
				correlationId: Guid.NewGuid(),
				eventId: Guid.NewGuid(),
				transactionPosition: 456,
				transactionOffset: 321,
				eventStreamId: "my-stream",
				eventStreamIdSize: null,
				expectedVersion: 789,
				timeStamp: DateTime.Now,
				flags: PrepareFlags.SingleWrite,
				eventType: "my-event-type",
				eventTypeSize: null,
				data: new byte[] { 0xDE, 0XAD, 0xC0, 0XDE },
				metadata: new byte[metadataBytesLength],
				properties: new byte[propertiesBytesLength],
				prepareRecordVersion: 2);
		}

		static CommitLogRecord CreateCommitLogRecord() => new(
			logPosition: 123,
			correlationId: Guid.NewGuid(),
			transactionPosition: 456,
			timeStamp: DateTime.Now,
			firstEventNumber: 789,
			commitRecordVersion: 1);

		static SystemLogRecord CreateSystemLogRecord() => new(logPosition: 123,
			timeStamp: DateTime.Now,
			systemRecordType: SystemRecordType.Epoch,
			systemRecordSerialization: SystemRecordSerialization.Binary,
			data: [0xDE, 0XAD, 0xC0, 0XDE]);
	}

	[Theory]
	[MemberData(nameof(GetLogRecords))]
	public void size_on_disk_is_correct(ILogRecord record) {

		var writer = new BufferWriterSlim<byte>(record.GetSizeWithLengthPrefixAndSuffix());
		try {
			const int dummyLength = 111;

			writer.WriteLittleEndian(dummyLength);
			record.WriteTo(ref writer);
			writer.WriteLittleEndian(dummyLength);

			Assert.Equal(writer.WrittenCount, record.GetSizeWithLengthPrefixAndSuffix());
		} finally {
			writer.Dispose();
		}
	}
}
