// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Runtime.InteropServices;

namespace KurrentDB.Core.Index;

[StructLayout(LayoutKind.Explicit)]
public readonly struct IndexEntry : IComparable<IndexEntry>, IEquatable<IndexEntry> {
	public const int IndexEntryV1Size = sizeof(int) + sizeof(int) + sizeof(long);
	public const int IndexEntryV2Size = sizeof(int) + sizeof(long) + sizeof(long);
	public const int IndexEntryV3And4Size = sizeof(long) + sizeof(long) + sizeof(long);

	[FieldOffset(0)] public readonly Int64 Version;
	[FieldOffset(8)] public readonly UInt64 Stream;
	[FieldOffset(16)] public readonly Int64 Position;

#if DEBUG
	static unsafe IndexEntry() {
		Debug.Assert(sizeof(IndexEntryV1) == IndexEntryV1Size);
		Debug.Assert(sizeof(IndexEntryV2) == IndexEntryV2Size);
		Debug.Assert(sizeof(IndexEntry) == IndexEntryV3And4Size);
	}
#endif

	public PTable.IndexEntryKey Key => new(Stream, Version);

	public IndexEntry(ulong stream, long version, long position) : this() {
		Stream = stream;
		Version = version;
		Position = position;
	}

	public static int GetSize(byte version) {
		return version switch {
			>= PTableVersions.IndexV3 => IndexEntryV3And4Size,
			PTableVersions.IndexV2 => IndexEntryV2Size,
			_ => IndexEntryV1Size
		};
	}

	public void AppendTo(Stream stream, byte ptableVersion) {
		if (ptableVersion <= PTableVersions.IndexV2) {
			if (ptableVersion == PTableVersions.IndexV2) {
				var entryV2 = new IndexEntryV2(this.Stream, (int)this.Version, this.Position);
				var buffer = MemoryMarshal.AsReadOnlyBytes(ref entryV2);
				Debug.Assert(buffer.Length == IndexEntryV2Size);
				stream.Write(buffer);
			} else {
				var entryV1 = new IndexEntryV1((uint)this.Stream, (int)this.Version, this.Position);
				var buffer = MemoryMarshal.AsReadOnlyBytes(ref entryV1);
				Debug.Assert(buffer.Length == IndexEntryV1Size);
				stream.Write(buffer);
			}

			return;
		}

		// v3+
		{
			var buffer = MemoryMarshal.AsReadOnlyBytes(in this);
			Debug.Assert(buffer.Length == IndexEntryV3And4Size);
			stream.Write(buffer);
			return;
		}
	}

	[SkipLocalsInit]
	public static IndexEntry ReadFrom(Stream stream, byte ptableVersion) {
		Debug.Assert(IndexEntryV3And4Size >= IndexEntryV2Size);
		Debug.Assert(IndexEntryV3And4Size >= IndexEntryV1Size);

		Span<byte> buffer = stackalloc byte[IndexEntryV3And4Size];
		var size = GetSize(ptableVersion);
		stream.ReadExactly(buffer[..size]);

		if (ptableVersion <= PTableVersions.IndexV2) {
			if (ptableVersion == PTableVersions.IndexV2) {
				ref readonly var entry = ref MemoryMarshal.AsRef<IndexEntryV2>(buffer);
				return new IndexEntry(entry.Stream, entry.Version, entry.Position);
			} else {
				ref readonly var entry = ref MemoryMarshal.AsRef<IndexEntryV1>(buffer);
				return new IndexEntry(entry.Stream, entry.Version, entry.Position);
			}
		}

		// v3+
		{
			return MemoryMarshal.Read<IndexEntry>(buffer);
		}
	}

	public int CompareTo(IndexEntry other) {
		var keyCmp = Stream.CompareTo(other.Stream);
		if (keyCmp != 0)
			return keyCmp;

		keyCmp = Version.CompareTo(other.Version);
		if (keyCmp != 0)
			return keyCmp;

		return Position.CompareTo(other.Position);
	}

	public bool Equals(IndexEntry other) {
		return (Stream == other.Stream && Version == other.Version) && Position == other.Position;
	}

	public override string ToString() {
		return string.Format("Stream: {0}, Version: {1}, Position: {2}", Stream, Version, Position);
	}

	[StructLayout(LayoutKind.Explicit, Pack = 4)]
	private readonly struct IndexEntryV2(ulong stream, int version, long position) {
		[FieldOffset(0)] public readonly Int32 Version = version;
		[FieldOffset(4)] public readonly UInt64 Stream = stream;
		[FieldOffset(12)] public readonly Int64 Position = position;
	}

	[StructLayout(LayoutKind.Explicit)]
	private readonly struct IndexEntryV1(uint stream, int version, long position) {
		[FieldOffset(0)] public readonly Int32 Version = version;
		[FieldOffset(4)] public readonly UInt32 Stream = stream;
		[FieldOffset(8)] public readonly Int64 Position = position;
	}
}
