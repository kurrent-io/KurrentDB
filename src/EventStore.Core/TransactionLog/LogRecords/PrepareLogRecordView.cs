// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.LogRecords;

// Use when parsing of a full prepare log record isn't required and only some bits need to be inspected.
// Note that the data structure is not aligned, so performance may degrade if heavily accessing properties.
// Designed to be reusable to avoid GC pressure when making a pass through the database.
public struct PrepareLogRecordView {
	public byte Version { get; }
	public long LogPosition => BitConverter.ToInt64(_record, 2);
	public PrepareFlags Flags => (PrepareFlags)BitConverter.ToUInt16(_record, 10);
	public long TransactionPosition => BitConverter.ToInt64(_record, 12);
	public int TransactionOffset => BitConverter.ToInt32(_record, 20);
	public long ExpectedVersion => _expectedVersion;
	public ReadOnlySpan<byte> EventStreamId => _record.AsSpan(_streamIdOffset, _streamIdSize);
	public Guid EventId => new Guid(_record.AsSpan(_eventIdOffset, 16).ToArray()); // allocates
	public Guid CorrelationId => new Guid(_record.AsSpan(_correlationIdOffset, 16).ToArray()); // allocates
	public DateTime TimeStamp => new DateTime(BitConverter.ToInt64(_record, _timestampOffset));
	public ReadOnlySpan<byte> EventType => _record.AsSpan(_eventTypeOffset, _eventTypeSize);
	public ReadOnlySpan<byte> Data => _record.AsSpan(_dataOffset, _dataSize);
	public ReadOnlySpan<byte> Metadata => _record.AsSpan(_metadataOffset, _metadataSize);

	private readonly byte[] _record;
	private readonly int _length;
	private readonly long _expectedVersion;
	private readonly int _streamIdSize;
	private readonly int _streamIdOffset;
	private readonly int _eventIdOffset;
	private readonly int _correlationIdOffset;
	private readonly int _timestampOffset;
	private readonly int _eventTypeSize;
	private readonly int _eventTypeOffset;
	private readonly int _dataSize;
	private readonly int _dataOffset;
	private readonly int _metadataSize;
	private readonly int _metadataOffset;

	public PrepareLogRecordView(byte[] record, int length) {
		if (!BitConverter.IsLittleEndian)
			throw new NotSupportedException();

		_record = record;
		_length = length;

		Version = _record[1];
		if (Version != LogRecordVersion.LogRecordV0 && Version != LogRecordVersion.LogRecordV1)
			throw new ArgumentException(
				$"PrepareRecord version {Version} is incorrect. Supported version: {PrepareLogRecord.PrepareRecordVersion}.");

		var currentOffset = 24;

		if (Version == LogRecordVersion.LogRecordV0) {
			int expectedVersion = BitConverter.ToInt32(_record, currentOffset);
			_expectedVersion = expectedVersion == int.MaxValue - 1 ? long.MaxValue - 1 : expectedVersion;
			currentOffset += 4;
		} else {
			_expectedVersion = BitConverter.ToInt64(_record, 24);
			currentOffset += 8;
		}

		_streamIdSize = Read7BitEncodedInt(_record.AsSpan(0, _length), ref currentOffset);
		_streamIdOffset = currentOffset;
		currentOffset += _streamIdSize;

		_eventIdOffset = currentOffset;
		currentOffset += 16;

		_correlationIdOffset = currentOffset;
		currentOffset += 16;

		_timestampOffset = currentOffset;
		currentOffset += 8;

		_eventTypeSize = Read7BitEncodedInt(_record.AsSpan(0, _length), ref currentOffset);
		_eventTypeOffset = currentOffset;
		currentOffset += _eventTypeSize;

		_dataSize = BitConverter.ToInt32(_record, currentOffset);
		currentOffset += 4;
		_dataOffset = currentOffset;
		currentOffset += _dataSize;

		_metadataSize = BitConverter.ToInt32(_record, currentOffset);
		currentOffset += 4;
		_metadataOffset = currentOffset;
		currentOffset += _metadataSize;

		if (currentOffset != _length) {
			throw new ArgumentException($"Unexpected record length: {currentOffset}, expected: {_length}");
		}

		// this is smaller than the actual record size but should be good enough to detect potential corruption
		// or reading at a wrong position
		if (_streamIdSize + _dataSize + _metadataSize > TFConsts.MaxLogRecordSize)
			throw new Exception("Record too large.");
	}

	public override string ToString() {
		return $"Version: {Version}, " +
		       $"LogPosition: {LogPosition}, " +
		       $"Flags: {Flags}, " +
		       $"TransactionPosition: {TransactionPosition}, " +
		       $"TransactionOffset: {TransactionOffset}, " +
		       $"ExpectedVersion: {ExpectedVersion}, " +
		       $"EventStreamId: {Encoding.UTF8.GetString(EventStreamId.ToArray())}, " +
		       $"EventId: {EventId}, " +
		       $"CorrelationId: {CorrelationId}, " +
		       $"TimeStamp: {TimeStamp}, " +
		       $"EventType: {Encoding.UTF8.GetString(EventType.ToArray())}, " +
		       $"Data size: {Data.Length}, " +
		       $"Metadata size: {Metadata.Length}";
	}

	// copied and adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/binaryreader.cs
	private static int Read7BitEncodedInt(ReadOnlySpan<byte> bytes, ref int offset) {
		// Read out an Int32 7 bits at a time.  The high bit
		// of the byte when on means to continue reading more bytes.
		int count = 0;
		int shift = 0;
		byte b;
		do {
			// Check for a corrupted stream.  Read a max of 5 bytes.
			// In a future version, add a DataFormatException.
			if (shift == 5 * 7)  // 5 bytes max per Int32, shift += 7
				throw new FormatException();

			b = bytes[offset++];
			count |= (b & 0x7F) << shift;
			shift += 7;
		} while ((b & 0x80) != 0);
		return count;
	}
}
