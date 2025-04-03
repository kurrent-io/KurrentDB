// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using EventStore.LogCommon;

namespace EventStore.LogV3;

public static class Raw {
	public const long SixMostSignificantBytes = unchecked((long)0xFF_FF_FF_FF_FF_FF_00_00);
	public const long TwoLeastSignificantBytes = unchecked(0x00_00_00_00_00_00_FF_FF);
	public const long SixLeastSignificantBytes = unchecked(0x00_00_FF_FF_FF_FF_FF_FF);
	public const long TwoMostSignificantBytes = unchecked((long)0xFF_FF_00_00_00_00_00_00);

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct RecordHeader {
		[FieldOffset(0)] private LogRecordType _type;
		[FieldOffset(1)] private byte _version;
		[FieldOffset(0)] private long _ticks;
		[FieldOffset(RecordIdOffset)] private Guid _recordId;

		// todo: remove this if possible
		[FieldOffset(RecordIdOffset + RecordIdSize)] private long _logPosition;
		public const int Size = 32;
		public const int RecordIdOffset = 8;
		public const int RecordIdSize = 16;

		public LogRecordType Type {
			get => _type;
			set => _type = value;
		}

		public byte Version {
			get => _version;
			set => _version = value;
		}

		public DateTime TimeStamp {
			// little-endian. blank out the 2 LEAST significant bytes
			get => new(_ticks & SixMostSignificantBytes);
			set => _ticks =
				(TwoLeastSignificantBytes & _ticks) |
				(SixMostSignificantBytes & value.Ticks);
		}

		public Guid RecordId {
			get => _recordId;
			set => _recordId = value;
		}

		public long LogPosition {
			get => _logPosition;
			set => _logPosition = value;
		}
	}

	[StructLayout(LayoutKind.Explicit, Size = RecordHeader.RecordIdSize, Pack = 1)]
	public struct StreamWriteId {
		// ~300 trillion
		public const long MaxStartingEventNumber = SixLeastSignificantBytes - 1;
		public const long EventNumberDeletedStream = long.MaxValue;

		[FieldOffset(0)] private ushort _topicNumber;
		[FieldOffset(2)] private ushort _categoryNumber;
		[FieldOffset(4)] private uint _streamNumber;
		[FieldOffset(8)] private long _startingEventNumber;
		[FieldOffset(14)] private ushort _parentTopicNumber;

		public ushort ParentTopicNumber {
			get => _parentTopicNumber;
			set => _parentTopicNumber = value;
		}

		public ushort TopicNumber {
			get => _topicNumber;
			set => _topicNumber = value;
		}

		public ushort CategoryNumber {
			get => _categoryNumber;
			set => _categoryNumber = value;
		}

		public uint StreamNumber {
			get => _streamNumber;
			set => _streamNumber = value;
		}

		public long StartingEventNumber {
			// little-endian. must not write the 2 MOST significant bytes
			get {
				var x = _startingEventNumber & SixLeastSignificantBytes;
				return x == SixLeastSignificantBytes ? EventNumberDeletedStream : x;
			}
			set {
				if (value == EventNumberDeletedStream) {
					_startingEventNumber |= SixLeastSignificantBytes;
					return;
				}

				if ((value | SixLeastSignificantBytes) != SixLeastSignificantBytes)
					throw new ArgumentOutOfRangeException(nameof(value), value, null);
				_startingEventNumber =
					(TwoMostSignificantBytes & _startingEventNumber) |
					value;
			}
		}
	}

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct EpochHeader {
		[FieldOffset(0)] private Guid _leaderInstanceId;
		[FieldOffset(16)] private long _prevEpochPosition;
		[FieldOffset(24)] private int _epochNumber;
		public const int Size = 28;

		public Guid LeaderInstanceId {
			get => _leaderInstanceId;
			set => _leaderInstanceId = value;
		}

		public long PrevEpochPosition {
			get => _prevEpochPosition;
			set => _prevEpochPosition = value;
		}

		public int EpochNumber {
			get => _epochNumber;
			set => _epochNumber = value;
		}
	}

	[Flags]
	public enum EventFlags : ushort {
	}

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct EventHeader {
		[FieldOffset(0)] private uint _eventTypeNumber;
		[FieldOffset(4)] private int _eventSize;
		[FieldOffset(8)] private int _systemMetadataSize;
		[FieldOffset(12)] private int _dataSize;
		[FieldOffset(16)] private EventFlags _flags;
		public const int Size = 18;

		public uint EventTypeNumber {
			get => _eventTypeNumber;
			set => _eventTypeNumber = value;
		}

		public EventFlags Flags {
			get => _flags;
			set => _flags = value;
		}

		public int EventSize {
			get => _eventSize;
			set => _eventSize = value;
		}

		public int SystemMetadataSize {
			get => _systemMetadataSize;
			set => _systemMetadataSize = value;
		}

		public int DataSize {
			get => _dataSize;
			set => _dataSize = value;
		}
	}

	[Flags]
	public enum PartitionFlags : ushort {
	}

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct PartitionHeader {
		[FieldOffset(0)] private Guid _partitionTypeId;
		[FieldOffset(16)] private Guid _parentPartitionId;
		[FieldOffset(32)] private PartitionFlags _flags;
		[FieldOffset(34)] private ushort _referenceNumber;
		public const int Size = 36;

		public Guid PartitionTypeId {
			get => _partitionTypeId;
			set => _partitionTypeId = value;
		}
		
		public Guid ParentPartitionId {
			get => _parentPartitionId;
			set => _parentPartitionId = value;
		}
		
		public PartitionFlags Flags {
			get => _flags;
			set => _flags = value;
		}

		public ushort ReferenceNumber {
			get => _referenceNumber;
			set => _referenceNumber = value;
		}
	}
	
	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct PartitionTypeHeader {
		[FieldOffset(0)] private Guid _partitionId;
		public const int Size = 16;

		public Guid PartitionId {
			get => _partitionId;
			set => _partitionId = value;
		}
	}

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct StreamHeader {
		[FieldOffset(0)] private Guid _partitionId;
		[FieldOffset(16)] private Guid _streamTypeId;
		[FieldOffset(32)] private uint _referenceNumber;
		public const int Size = 36;

		public Guid PartitionId {
			get => _partitionId;
			set => _partitionId = value;
		}

		public Guid StreamTypeId {
			get => _streamTypeId;
			set => _streamTypeId = value;
		}

		public uint ReferenceNumber {
			get => _referenceNumber;
			set => _referenceNumber = value;
		}
	}

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct StreamTypeHeader {
		[FieldOffset(0)] private Guid _partitionId;
		public const int Size = 16;

		public Guid PartitionId {
			get => _partitionId;
			set => _partitionId = value;
		}
	}
	
	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct EventTypeHeader {
		[FieldOffset(0)] private Guid _parentEventTypeId;
		[FieldOffset(16)] private Guid _partitionId;
		[FieldOffset(32)] private uint _referenceNumber;
		[FieldOffset(36)] private ushort _version;
		
		public const int Size = 38;

		public Guid ParentEventTypeId {
			get => _parentEventTypeId;
			set => _parentEventTypeId = value;
		}

		public Guid PartitionId {
			get => _partitionId;
			set => _partitionId = value;
		}
		
		public uint ReferenceNumber {
			get => _referenceNumber;
			set => _referenceNumber = value;
		}

		public ushort Version {
			get => _version;
			set => _version = value;
		}
	}

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct ContentTypeHeader {
		[FieldOffset(0)] private Guid _partitionId;
		[FieldOffset(16)] private ushort _referenceNumber;

		public const int Size = 18;

		public Guid PartitionId {
			get => _partitionId;
			set => _partitionId = value;
		}

		public ushort ReferenceNumber {
			get => _referenceNumber;
			set => _referenceNumber = value;
		}
	}

	[Flags]
	public enum StreamWriteFlags : ushort {
	}

	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct StreamWriteHeader {
		[FieldOffset(0)] private StreamWriteFlags _flags;
		[FieldOffset(2)] private short _count;
		[FieldOffset(4)] private int _metadataSize;
		public const int Size = 8;

		public StreamWriteFlags Flags {
			get => _flags;
			set => _flags = value;
		}

		public short Count {
			get => _count;
			set => _count = value;
		}

		public int MetadataSize {
			get => _metadataSize;
			set => _metadataSize = value;
		}
	}
	
	public enum TransactionStatus : byte {
		Prepare = 0,
		Committed = 1,
		Failed = 2,
		Removed = 3
	}
	
	public enum TransactionType : byte {
		StandardWrite = 0,
		MultipartTransaction = 1
	}
	
	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct TransactionStartHeader {
		[FieldOffset(0)] private uint _recordCount;
		[FieldOffset(4)] private TransactionStatus _status;
		[FieldOffset(5)] private TransactionType _type;
		public const int Size = 6;

		public TransactionStatus Status {
			get => _status;
			set => _status = value;
		}

		public TransactionType Type {
			get => _type;
			set => _type = value;
		}

		public uint RecordCount {
			get => _recordCount;
			set => _recordCount = value;
		}
	}
	
	[StructLayout(LayoutKind.Explicit, Size = Size, Pack = 1)]
	public struct TransactionEndHeader {
		[FieldOffset(0)] private uint _recordCount;
		public const int Size = 4;
		
		public uint RecordCount {
			get => _recordCount;
			set => _recordCount = value;
		}
	}
}
