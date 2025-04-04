// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;

namespace EventStore.Core;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct IndexEntryV2 : IComparable<IndexEntryV2>, IEquatable<IndexEntryV2> {
	[FieldOffset(0)] public fixed byte Bytes[20];
	[FieldOffset(0)] public Int32 Version;
	[FieldOffset(4)] public UInt64 Stream;
	[FieldOffset(12)] public Int64 Position;

	public IndexEntryV2(ulong stream, int version, long position) : this() {
		Stream = stream;
		Version = version;
		Position = position;
	}

	public int CompareTo(IndexEntryV2 other) {
		var keyCmp = Stream.CompareTo(other.Stream);
		if (keyCmp == 0) {
			keyCmp = Version.CompareTo(other.Version);
			if (keyCmp != 0)
				return keyCmp;
		}

		if (keyCmp != 0)
			return keyCmp;
		return Position.CompareTo(other.Position);
	}

	public bool Equals(IndexEntryV2 other) {
		return (Stream == other.Stream && Version == other.Version) && Position == other.Position;
	}

	public override string ToString() {
		return string.Format("Stream: {0}, Version: {1}, Position: {2}", Stream, Version, Position);
	}
}
