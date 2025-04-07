// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.LogAbstraction;
using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.LogRecords;

public abstract class LogRecord : ILogRecord {
	public static readonly ReadOnlyMemory<byte> NoData = Empty.ByteArray;

	public LogRecordType RecordType { get; }
	public byte Version { get; }
	public long LogPosition { get; }

	public long GetNextLogPosition(long logicalPosition, int length) {
		return logicalPosition + length + 2 * sizeof(int);
	}

	public long GetPrevLogPosition(long logicalPosition, int length) {
		return logicalPosition - length - 2 * sizeof(int);
	}

	public static ILogRecord ReadFrom(BinaryReader reader, int length) {
		var recordType = (LogRecordType)reader.ReadByte();
		var version = reader.ReadByte();

		static long ReadPosition(BinaryReader reader) {
			var logPosition = reader.ReadInt64();
			Ensure.Nonnegative(logPosition, "logPosition");
			return logPosition;
		}

		switch (recordType) {
			case LogRecordType.Prepare:
				return new PrepareLogRecord(reader, version, ReadPosition(reader));
			case LogRecordType.Commit:
				return new CommitLogRecord(reader, version, ReadPosition(reader));
			case LogRecordType.System:
				if (version > SystemLogRecord.SystemRecordVersion)
					return new LogV3EpochLogRecord(LogV3Reader.ReadBytes(recordType, version, reader, length));

				return new SystemLogRecord(reader, version, ReadPosition(reader));

			case LogRecordType.StreamWrite:
				return new LogV3StreamWriteRecord(LogV3Reader.ReadBytes(recordType, version, reader, length));

			case LogRecordType.Stream:
				return new LogV3StreamRecord(LogV3Reader.ReadBytes(recordType, version, reader, length));

			case LogRecordType.EventType:
				return new LogV3EventTypeRecord(LogV3Reader.ReadBytes(recordType, version, reader, length));

			case LogRecordType.PartitionType:
				return new PartitionTypeLogRecord(LogV3Reader.ReadBytes(recordType, version, reader, length));

			case LogRecordType.Partition:
				return new PartitionLogRecord(LogV3Reader.ReadBytes(recordType, version, reader, length));

			default:
				throw new ArgumentOutOfRangeException("recordType");
		}
	}

	public static IPrepareLogRecord<TStreamId> Prepare<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId, long transactionPos,
		int transactionOffset,
		TStreamId eventStreamId, long expectedVersion, PrepareFlags flags, TStreamId eventType,
		ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata, DateTime? timeStamp = null) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, transactionPos, transactionOffset,
			eventStreamId, expectedVersion, timeStamp ?? DateTime.UtcNow, flags, eventType,
			data, metadata);
	}

	public static CommitLogRecord Commit(long logPosition, Guid correlationId, long startPosition,
		long eventNumber) {
		return new CommitLogRecord(logPosition, correlationId, startPosition, DateTime.UtcNow, eventNumber);
	}

	public static IPrepareLogRecord<TStreamId> SingleWrite<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId,
		TStreamId eventStreamId,
		long expectedVersion, TStreamId eventType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata,
		DateTime? timestamp = null, PrepareFlags? additionalFlags = null) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, logPosition, 0, eventStreamId,
			expectedVersion,
			timestamp ?? DateTime.UtcNow,
			PrepareFlags.Data | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
			(additionalFlags ?? PrepareFlags.None),
			eventType, data, metadata);
	}

	public static IPrepareLogRecord<TStreamId> TransactionBegin<TStreamId>(IRecordFactory<TStreamId> factory, long logPos, Guid correlationId, TStreamId eventStreamId,
		long expectedVersion) {
		return factory.CreatePrepare(logPos, correlationId, Guid.NewGuid(), logPos, -1, eventStreamId,
			expectedVersion,
			DateTime.UtcNow, PrepareFlags.TransactionBegin, default, NoData, NoData);
	}

	public static IPrepareLogRecord<TStreamId> TransactionWrite<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId,
		long transactionPos, int transactionOffset, TStreamId eventStreamId, TStreamId eventType, byte[] data,
		byte[] metadata, bool isJson) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, transactionPos, transactionOffset,
			eventStreamId, ExpectedVersion.Any, DateTime.UtcNow,
			PrepareFlags.Data | (isJson ? PrepareFlags.IsJson : PrepareFlags.None),
			eventType, data, metadata);
	}

	public static IPrepareLogRecord<TStreamId> TransactionEnd<TStreamId>(IRecordFactory<TStreamId> factory, long logPos, Guid correlationId, Guid eventId,
		long transactionPos, TStreamId eventStreamId) {
		return factory.CreatePrepare(logPos, correlationId, eventId, transactionPos, -1, eventStreamId,
			ExpectedVersion.Any,
			DateTime.UtcNow, PrepareFlags.TransactionEnd, default, NoData, NoData);
	}

	public static IPrepareLogRecord<TStreamId> DeleteTombstone<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId,
		TStreamId eventStreamId, TStreamId eventType, long expectedVersion, PrepareFlags additionalFlags = PrepareFlags.None) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, logPosition, 0, eventStreamId,
			expectedVersion, DateTime.UtcNow,
			PrepareFlags.StreamDelete | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
			additionalFlags,
			eventType, NoData, NoData);
	}

	protected LogRecord(LogRecordType recordType, byte version, long logPosition) {
		Ensure.Nonnegative(logPosition, "logPosition");
		RecordType = recordType;
		Version = version;
		LogPosition = logPosition;
	}

	public virtual void WriteTo(BinaryWriter writer) {
		writer.Write((byte)RecordType);
		writer.Write(Version);
		writer.Write(LogPosition);
	}

	public int GetSizeWithLengthPrefixAndSuffix() {
		using (var memoryStream = new MemoryStream()) {
			WriteTo(new BinaryWriter(memoryStream));
			return 8 + (int)memoryStream.Length;
		}
	}
}
