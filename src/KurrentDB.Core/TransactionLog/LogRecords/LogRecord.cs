// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using DotNext.Buffers;
using DotNext.Buffers.Binary;
using DotNext.IO;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.LogCommon;

namespace KurrentDB.Core.TransactionLog.LogRecords;

public abstract class LogRecord : ILogRecord {
	public static readonly ReadOnlyMemory<byte> NoData = Empty.ByteArray;

	// RecordType + Version + LogPosition
	protected const int BaseSize = sizeof(byte) + sizeof(byte) + sizeof(long);

	public LogRecordType RecordType { get; }
	public byte Version { get; }
	public long LogPosition { get; }

	public long GetNextLogPosition(long logicalPosition, int length) {
		return logicalPosition + length + 2 * sizeof(int);
	}

	public long GetPrevLogPosition(long logicalPosition, int length) {
		return logicalPosition - length - 2 * sizeof(int);
	}

	public static ILogRecord ReadFrom(ref SequenceReader reader) {
		var header = reader.Read<Header>();

		switch (header.Type) {
			case LogRecordType.Prepare:
				var logPosition = reader.ReadLittleEndian<long>();
				Ensure.Nonnegative(logPosition, nameof(logPosition));
				return new PrepareLogRecord(ref reader, header.Version, logPosition);

			case LogRecordType.Commit:
				logPosition = reader.ReadLittleEndian<long>();
				Ensure.Nonnegative(logPosition, nameof(logPosition));
				return new CommitLogRecord(ref reader, header.Version, logPosition);

			case LogRecordType.System when header.Version > SystemLogRecord.SystemRecordVersion:
				reader.Reset();
				return new LogV3EpochLogRecord(reader.ReadToEnd().ToArray());

			case LogRecordType.System:
				logPosition = reader.ReadLittleEndian<long>();
				Ensure.Nonnegative(logPosition, nameof(logPosition));
				return new SystemLogRecord(ref reader, header.Version, logPosition);

			case LogRecordType.StreamWrite:
				reader.Reset();
				return new LogV3StreamWriteRecord(reader.ReadToEnd().ToArray());

			case LogRecordType.Stream:
				reader.Reset();
				return new LogV3StreamRecord(reader.ReadToEnd().ToArray());

			case LogRecordType.EventType:
				reader.Reset();
				return new LogV3EventTypeRecord(reader.ReadToEnd().ToArray());

			case LogRecordType.PartitionType:
				reader.Reset();
				return new PartitionTypeLogRecord(reader.ReadToEnd().ToArray());

			case LogRecordType.Partition:
				reader.Reset();
				return new PartitionLogRecord(reader.ReadToEnd().ToArray());

			default:
				throw new ArgumentOutOfRangeException("recordType");
		}
	}

	public static IPrepareLogRecord<TStreamId> Prepare<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId, long transactionPos,
		int transactionOffset,
		TStreamId eventStreamId, long expectedVersion, PrepareFlags flags, TStreamId eventType,
		ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata,
        SchemaInfo dataSchemaInfo, SchemaInfo metadataSchemaInfo,
        DateTime? timeStamp = null) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, transactionPos, transactionOffset,
			eventStreamId, expectedVersion, timeStamp ?? DateTime.UtcNow, flags, eventType,
			data, metadata, dataSchemaInfo, metadataSchemaInfo);
	}

	public static IPrepareLogRecord<TStreamId> Prepare<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId, long transactionPos,
		int transactionOffset,
		TStreamId eventStreamId, long expectedVersion, PrepareFlags flags, TStreamId eventType,
		ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata,
		DateTime? timeStamp = null) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, transactionPos, transactionOffset,
			eventStreamId, expectedVersion, timeStamp ?? DateTime.UtcNow, flags, eventType,
			data, metadata, SchemaInfo.None, SchemaInfo.None);
	}

	public static CommitLogRecord Commit(long logPosition, Guid correlationId, long startPosition,
		long eventNumber) {
		return new CommitLogRecord(logPosition, correlationId, startPosition, DateTime.UtcNow, eventNumber);
	}

	public static IPrepareLogRecord<TStreamId> SingleWrite<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId,
		TStreamId eventStreamId,
		long expectedVersion, TStreamId eventType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata,
        SchemaInfo dataSchemaInfo, SchemaInfo metadataSchemaInfo,
		DateTime? timestamp = null, PrepareFlags? additionalFlags = null) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, logPosition, 0, eventStreamId,
			expectedVersion,
			timestamp ?? DateTime.UtcNow,
			PrepareFlags.Data | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
			(additionalFlags ?? PrepareFlags.None),
			eventType, data, metadata, dataSchemaInfo, metadataSchemaInfo);
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
			eventType, data, metadata, SchemaInfo.None, SchemaInfo.None);
	}

	public static IPrepareLogRecord<TStreamId> TransactionBegin<TStreamId>(IRecordFactory<TStreamId> factory, long logPos, Guid correlationId, TStreamId eventStreamId,
		long expectedVersion) {
		return factory.CreatePrepare(logPos, correlationId, Guid.NewGuid(), logPos, -1, eventStreamId,
			expectedVersion,
			DateTime.UtcNow, PrepareFlags.TransactionBegin, default, NoData, NoData, SchemaInfo.None, SchemaInfo.None);
	}

	public static IPrepareLogRecord<TStreamId> TransactionWrite<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId,
		long transactionPos, int transactionOffset, TStreamId eventStreamId, TStreamId eventType, byte[] data,
		byte[] metadata, SchemaInfo dataSchemaInfo, SchemaInfo metadataSchemaInfo, bool isJson) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, transactionPos, transactionOffset,
			eventStreamId, ExpectedVersion.Any, DateTime.UtcNow,
			PrepareFlags.Data | (isJson ? PrepareFlags.IsJson : PrepareFlags.None),
			eventType, data, metadata, dataSchemaInfo, metadataSchemaInfo);
	}

	public static IPrepareLogRecord<TStreamId> TransactionEnd<TStreamId>(IRecordFactory<TStreamId> factory, long logPos, Guid correlationId, Guid eventId,
		long transactionPos, TStreamId eventStreamId) {
		return factory.CreatePrepare(logPos, correlationId, eventId, transactionPos, -1, eventStreamId,
			ExpectedVersion.Any,
			DateTime.UtcNow, PrepareFlags.TransactionEnd, default, NoData, NoData, SchemaInfo.None, SchemaInfo.None);
	}

	public static IPrepareLogRecord<TStreamId> DeleteTombstone<TStreamId>(IRecordFactory<TStreamId> factory, long logPosition, Guid correlationId, Guid eventId,
		TStreamId eventStreamId, TStreamId eventType, long expectedVersion, PrepareFlags additionalFlags = PrepareFlags.None) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, logPosition, 0, eventStreamId,
			expectedVersion, DateTime.UtcNow,
			PrepareFlags.StreamDelete | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
			additionalFlags,
			eventType, NoData, NoData, SchemaInfo.None, SchemaInfo.None);
	}

	protected LogRecord(LogRecordType recordType, byte version, long logPosition) {
		Ensure.Nonnegative(logPosition, "logPosition");
		RecordType = recordType;
		Version = version;
		LogPosition = logPosition;
	}

	public virtual void WriteTo(ref BufferWriterSlim<byte> writer) {
		writer.Add((byte)RecordType);
		writer.Add(Version);
		writer.WriteLittleEndian(LogPosition);
	}

	public abstract int GetSizeWithLengthPrefixAndSuffix();

	private readonly struct Header : IBinaryFormattable<Header> {
		private const int Size = sizeof(LogRecordType) + sizeof(byte);
		internal readonly LogRecordType Type;
		internal readonly byte Version;

		private Header(ReadOnlySpan<byte> input) {
			// Perf: Read the span from the last element to have just one range check inserted by JIT
			Version = input[1];
			Type = (LogRecordType)input[0];
		}

		public void Format(Span<byte> output) {
			output[1] = Version;
			output[0] = (byte)Type;
		}

		static int IBinaryFormattable<Header>.Size => Size;

		static Header IBinaryFormattable<Header>.Parse(ReadOnlySpan<byte> input)
			=> new(input);
	}
}
