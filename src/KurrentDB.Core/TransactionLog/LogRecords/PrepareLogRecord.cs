// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DotNext.Buffers;
using DotNext.Buffers.Binary;
using DotNext.Collections.Generic;
using DotNext.IO;
using DotNext.Text;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.LogCommon;
using JetBrains.Annotations;

namespace KurrentDB.Core.TransactionLog.LogRecords;

[Flags]
public enum PrepareFlags : ushort {
	None = 0x00,
	Data = 0x01, // prepare contains data
	TransactionBegin = 0x02, // prepare starts transaction
	TransactionEnd = 0x04, // prepare ends transaction
	StreamDelete = 0x08, // prepare deletes stream

	IsCommitted = 0x20, // prepare should be considered committed immediately, no commit will follow in TF
						//Update = 0x30,                  // prepare updates previous instance of the same event, DANGEROUS!

	IsJson = 0x100, // indicates data & metadata are valid json
	IsRedacted = 0x200,
	HasDataSchema = 0x400,
	HasMetadataSchema = 0x800,

	// aggregate flag set
	// unused and easily confused with StreamDelete:  DeleteTombstone = TransactionBegin | TransactionEnd | StreamDelete,
	SingleWrite = Data | TransactionBegin | TransactionEnd
}

public static class PrepareFlagsExtensions {
	public static bool HasAllOf(this PrepareFlags flags, PrepareFlags flagSet) {
		return (flags & flagSet) == flagSet;
	}

	public static bool HasAnyOf(this PrepareFlags flags, PrepareFlags flagSet) {
		return (flags & flagSet) != 0;
	}

	public static bool HasNoneOf(this PrepareFlags flags, PrepareFlags flagSet) {
		return (flags & flagSet) == 0;
	}
}

public sealed class PrepareLogRecord : LogRecord, IEquatable<PrepareLogRecord>, IPrepareLogRecord<string> {
	public const byte PrepareRecordVersion = 1;

	public PrepareFlags Flags { get; }
	public long TransactionPosition { get; }
	public int TransactionOffset { get; }
	public long ExpectedVersion { get; } // if IsCommitted is set, this is final EventNumber
	public string EventStreamId { get; }
	private int? _eventStreamIdSize;
	public Guid EventId { get; }
	public Guid CorrelationId { get; }
	public DateTime TimeStamp { get; }
	public string EventType { get; }
	private int? _eventTypeSize;
	public ReadOnlyMemory<byte> Data => Flags.HasFlag(PrepareFlags.IsRedacted) ? NoData : _dataOnDisk;
	private readonly ReadOnlyMemory<byte> _dataOnDisk;

    public ReadOnlyMemory<byte> Metadata { get; }
    public SchemaInfo DataSchemaInfo => _dataSchemaInfo;
    public SchemaInfo MetadataSchemaInfo => _metadataSchemaInfo;

    public string ContentType { get; set; }

	private int? _sizeOnDisk;

	public long InMemorySize {
		get {
			return sizeof(LogRecordType)
				   + 1
				   + 8
				   + sizeof(PrepareFlags)
				   + 8
				   + 4
				   + 4
				   + IntPtr.Size + EventStreamId.Length * 2
				   + 16
				   + 16
				   + 8
				   + IntPtr.Size + EventType.Length * 2
				   + IntPtr.Size + _dataOnDisk.Length + SchemaInfo.ByteSize
				   + IntPtr.Size + Metadata.Length + SchemaInfo.ByteSize;
		}
	}

    private readonly SchemaInfo _dataSchemaInfo;
    private readonly SchemaInfo _metadataSchemaInfo;

	// including length and suffix
	private int ComputeSizeOnDisk() {
		int eventStreamIdSize = _eventStreamIdSize ??= Encoding.UTF8.GetByteCount(EventStreamId);
		int eventTypeSize = _eventTypeSize ??= Encoding.UTF8.GetByteCount(EventType);

		return
			2 * sizeof(int) /* Length prefix & suffix */
			+ BaseSize
			+ sizeof(ushort) /* Flags */
			+ sizeof(long) /* TransactionPosition */
			+ sizeof(int) /* TransactionOffset */
			+ (Version is LogRecordVersion.LogRecordV0 ? sizeof(int) : sizeof(long)) /* ExpectedVersion */
			+ StringSizeWithLengthPrefix(eventStreamIdSize) /* EventStreamId */
			+ 16 /* EventId */
			+ 16 /* CorrelationId */
			+ sizeof(long) /* TimeStamp */
			+ StringSizeWithLengthPrefix(eventTypeSize) /* EventType */
			+ sizeof(int) /* Data length */
			+ (_dataSchemaInfo != SchemaInfo.None ? SchemaInfo.ByteSize : 0)
			+ _dataOnDisk.Length /* Data */
			+ sizeof(int) /* Metadata length */
			+ (_metadataSchemaInfo != SchemaInfo.None ? SchemaInfo.ByteSize : 0)
			+ Metadata.Length; /* Metadata */

		// when written to disk, a string is prefixed with its length which is written as ULEB128
		static int StringSizeWithLengthPrefix(int stringSize) {
			Debug.Assert(stringSize >= 0);

			return stringSize is 0 ? 1 : stringSize + int.Log2(stringSize) / 7 + 1;
		}
	}


	public PrepareLogRecord(long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		string eventStreamId,
		int? eventStreamIdSize,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		string eventType,
		int? eventTypeSize,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> metadata,
        SchemaInfo dataSchemaInfo,
        SchemaInfo metadataSchemaInfo,
		byte prepareRecordVersion = PrepareRecordVersion
   )

		: base(LogRecordType.Prepare, prepareRecordVersion, logPosition) {
		Ensure.NotEmptyGuid(correlationId, "correlationId");
		Ensure.NotEmptyGuid(eventId, "eventId");
		Ensure.Nonnegative(transactionPosition, "transactionPosition");
		if (transactionOffset < -1)
			throw new ArgumentOutOfRangeException("transactionOffset");
		Ensure.NotNullOrEmpty(eventStreamId, "eventStreamId");
		if (expectedVersion < Core.Data.ExpectedVersion.Any)
			throw new ArgumentOutOfRangeException("expectedVersion");

		Flags = flags;
		TransactionPosition = transactionPosition;
		TransactionOffset = transactionOffset;
		ExpectedVersion = expectedVersion;
		EventStreamId = eventStreamId;
		_eventStreamIdSize = eventStreamIdSize;

		EventId = eventId;
		CorrelationId = correlationId;
		TimeStamp = timeStamp;
		EventType = eventType ?? string.Empty;
		_eventTypeSize = eventTypeSize;
		_dataOnDisk = data;
        _dataSchemaInfo = dataSchemaInfo ?? SchemaInfo.None;
        _metadataSchemaInfo = metadataSchemaInfo ?? SchemaInfo.None;
        Metadata = metadata;
		if (InMemorySize > TFConsts.MaxLogRecordSize)
			throw new Exception("Record too large.");
	}

	public PrepareLogRecord(long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		string eventStreamId,
		int? eventStreamIdSize,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		string eventType,
		int? eventTypeSize,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> metadata,
		byte prepareRecordVersion = PrepareRecordVersion
	) : this(logPosition,
		correlationId,
		eventId,
		transactionPosition,
		transactionOffset,
		eventStreamId,
		eventStreamIdSize,
		expectedVersion,
		timeStamp,
		flags,
		eventType,
		eventTypeSize,
		data,
		metadata,
		SchemaInfo.None,
		SchemaInfo.None,
		prepareRecordVersion) { }

	internal PrepareLogRecord(ref SequenceReader reader, byte version, long logPosition)
		: base(LogRecordType.Prepare, version, logPosition) {
		if (version is not LogRecordVersion.LogRecordV0 and not LogRecordVersion.LogRecordV1)
			throw new ArgumentException(
				$"PrepareRecord version {version} is incorrect. Supported version: {PrepareRecordVersion}.");

		_dataSchemaInfo = SchemaInfo.None;
		_metadataSchemaInfo = SchemaInfo.None;
		var context = new DecodingContext(Encoding.UTF8, reuseDecoder: true);
		Flags = (PrepareFlags)reader.ReadLittleEndian<ushort>();
		TransactionPosition = reader.ReadLittleEndian<long>();
		TransactionOffset = reader.ReadLittleEndian<int>();
		ExpectedVersion = version is LogRecordVersion.LogRecordV0
			? AdjustVersion(reader.ReadLittleEndian<int>())
			: reader.ReadLittleEndian<long>();
		EventStreamId = ReadString(ref reader, in context);
		EventId = reader.Read<Blittable<Guid>>().Value;
		CorrelationId = reader.Read<Blittable<Guid>>().Value;
		TimeStamp = new(reader.ReadLittleEndian<long>());
		EventType = ReadString(ref reader, in context);


        _dataOnDisk = reader.ReadLittleEndian<int>() is var dataCount && dataCount > 0
			? ReadDataContent(ref reader, dataCount, Flags.HasFlag(PrepareFlags.HasDataSchema), out _dataSchemaInfo)
			: NoData;

		Metadata = reader.ReadLittleEndian<int>() is var metadataCount && metadataCount > 0
			? ReadDataContent(ref reader, metadataCount, Flags.HasFlag(PrepareFlags.HasMetadataSchema), out _metadataSchemaInfo)
			: NoData;

		if (InMemorySize > TFConsts.MaxLogRecordSize)
			throw new Exception("Record too large.");

		static long AdjustVersion(int version)
			=> version is int.MaxValue - 1 ? long.MaxValue - 1L : version;

		static string ReadString(ref SequenceReader reader, in DecodingContext context) {
			using var buffer = reader.Decode(in context, LengthFormat.Compressed);
			return buffer.ToString();
		}
	}

	public IPrepareLogRecord<string> CopyForRetry(long logPosition, long transactionPosition) {
		return new PrepareLogRecord(
			logPosition: logPosition,
			correlationId: CorrelationId,
			eventId: EventId,
			transactionPosition: transactionPosition,
			transactionOffset: TransactionOffset,
			eventStreamId: EventStreamId,
			eventStreamIdSize: _eventStreamIdSize,
			expectedVersion: ExpectedVersion,
			timeStamp: TimeStamp,
			flags: Flags,
			eventType: EventType,
			eventTypeSize: _eventTypeSize,
			data: _dataOnDisk,
			metadata: Metadata,
            dataSchemaInfo: _dataSchemaInfo,
            metadataSchemaInfo: _metadataSchemaInfo
        );
	}

	public override void WriteTo(ref BufferWriterSlim<byte> writer) {
		base.WriteTo(ref writer);

		var finalFlags = Flags;
		if (_dataSchemaInfo != SchemaInfo.None)
			finalFlags |= PrepareFlags.HasDataSchema;

		if (_metadataSchemaInfo != SchemaInfo.None)
			finalFlags |= PrepareFlags.HasMetadataSchema;

		writer.WriteLittleEndian((ushort)finalFlags);
		writer.WriteLittleEndian(TransactionPosition);
		writer.WriteLittleEndian(TransactionOffset);
		if (Version is LogRecordVersion.LogRecordV0) {
			int expectedVersion = ExpectedVersion == long.MaxValue - 1 ? int.MaxValue - 1 : (int)ExpectedVersion;
			writer.WriteLittleEndian(expectedVersion);
		} else {
			writer.WriteLittleEndian(ExpectedVersion);
		}

		_eventStreamIdSize ??= Encoding.UTF8.GetByteCount(EventStreamId);

		// 7-bit encoded int from BinaryWriter is actually ULEB128, so we need to cast int to uint
		// first to preserve binary compatibility
		writer.WriteLeb128((uint)_eventStreamIdSize.GetValueOrDefault());
		var buffer = writer.GetSpan(_eventStreamIdSize.GetValueOrDefault());
		writer.Advance(Encoding.UTF8.GetBytes(EventStreamId, buffer));

		buffer = writer.GetSpan(16);
		EventId.TryWriteBytes(buffer);
		writer.Advance(16);

		buffer = writer.GetSpan(16);
		CorrelationId.TryWriteBytes(buffer);
		writer.Advance(16);

		writer.WriteLittleEndian(TimeStamp.Ticks);

		_eventTypeSize ??= Encoding.UTF8.GetByteCount(EventType);
		writer.WriteLeb128((uint)_eventTypeSize.GetValueOrDefault());
		buffer = writer.GetSpan(_eventTypeSize.GetValueOrDefault());
		writer.Advance(Encoding.UTF8.GetBytes(EventType, buffer));


        var finalDataOnDisk = WrapSchemaInfo(_dataSchemaInfo, _dataOnDisk);
        var finalMetadata = WrapSchemaInfo(_metadataSchemaInfo, Metadata);

		writer.Write(finalDataOnDisk.Span, LengthFormat.LittleEndian);
		writer.Write(finalMetadata.Span, LengthFormat.LittleEndian);
	}

	public override int GetSizeWithLengthPrefixAndSuffix()
		=> _sizeOnDisk ??= ComputeSizeOnDisk();

	public bool Equals(PrepareLogRecord other) {
		if (ReferenceEquals(null, other))
			return false;
		if (ReferenceEquals(this, other))
			return true;
		return other.LogPosition == LogPosition
			   && other.Flags == Flags
			   && other.TransactionPosition == TransactionPosition
			   && other.TransactionOffset == TransactionOffset
			   && other.ExpectedVersion == ExpectedVersion
			   && other.EventStreamId.Equals(EventStreamId)
			   && other.EventId == EventId
			   && other.CorrelationId == CorrelationId
			   && other.TimeStamp.Equals(TimeStamp)
			   && other.EventType.Equals(EventType)
			   && other.Data.Span.SequenceEqual(Data.Span)
			   && other._dataOnDisk.Span.SequenceEqual(_dataOnDisk.Span)
			   && other.Metadata.Span.SequenceEqual(Metadata.Span);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj))
			return false;
		if (ReferenceEquals(this, obj))
			return true;
		if (obj.GetType() != typeof(PrepareLogRecord))
			return false;
		return Equals((PrepareLogRecord)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int result = LogPosition.GetHashCode();
			result = (result * 397) ^ Flags.GetHashCode();
			result = (result * 397) ^ TransactionPosition.GetHashCode();
			result = (result * 397) ^ TransactionOffset;
			result = (result * 397) ^ ExpectedVersion.GetHashCode();
			result = (result * 397) ^ EventStreamId.GetHashCode();

			result = (result * 397) ^ EventId.GetHashCode();
			result = (result * 397) ^ CorrelationId.GetHashCode();
			result = (result * 397) ^ TimeStamp.GetHashCode();
			result = (result * 397) ^ EventType.GetHashCode();
			result = (result * 397) ^ _dataOnDisk.GetHashCode();
			result = (result * 397) ^ Metadata.GetHashCode();
			return result;
		}
	}

	public static bool operator ==(PrepareLogRecord left, PrepareLogRecord right) {
		return Equals(left, right);
	}

	public static bool operator !=(PrepareLogRecord left, PrepareLogRecord right) {
		return !Equals(left, right);
	}

	public override string ToString() {
		return string.Format("LogPosition: {0}, "
							 + "Flags: {1}, "
							 + "TransactionPosition: {2}, "
							 + "TransactionOffset: {3}, "
							 + "ExpectedVersion: {4}, "
							 + "EventStreamId: {5}, "
							 + "EventId: {6}, "
							 + "CorrelationId: {7}, "
							 + "TimeStamp: {8}, "
							 + "EventType: {9}, "
							 + "InMemorySize: {10}",
			LogPosition,
			Flags,
			TransactionPosition,
			TransactionOffset,
			ExpectedVersion,
			EventStreamId,
			EventId,
			CorrelationId,
			TimeStamp,
			EventType,
			InMemorySize);
	}

	public static ReadOnlyMemory<byte> WrapSchemaInfo(SchemaInfo schema, ReadOnlyMemory<byte> payload) {
		if (schema == SchemaInfo.None)
			return payload;

		var buffer = new byte[SchemaInfo.ByteSize + payload.Length];
		schema.Write(0, buffer);

		Memory<byte> mem = buffer;
		payload.Span.CopyTo(mem.Span[SchemaInfo.ByteSize..]);

		return mem;
	}

	private static byte[] ReadDataContent(ref SequenceReader reader, int count, bool parseSchema, out SchemaInfo info) {
		info = SchemaInfo.None;
		var content = reader.Read(count);

		if (!parseSchema)
			return content.ToArray();

		var local = new SequenceReader(content);
		info = SchemaInfo.Read(ref local);

		return local.RemainingSequence.ToArray();
	}

}
