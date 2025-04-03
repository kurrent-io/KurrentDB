// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;

namespace EventStore.Core.Index;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct IndexEntryV1 : IComparable<IndexEntryV1>, IEquatable<IndexEntryV1> {
	[FieldOffset(0)] public UInt64 Key;
	[FieldOffset(0)] public fixed byte Bytes [16];
	[FieldOffset(0)] public Int32 Version;
	[FieldOffset(4)] public UInt32 Stream;
	[FieldOffset(8)] public Int64 Position;

	public IndexEntryV1(ulong key, long position) : this() {
		Key = key;
		Position = position;
	}

	public IndexEntryV1(uint stream, int version, long position) : this() {
		Stream = stream;
		Version = version;
		Position = position;
	}

	public int CompareTo(IndexEntryV1 other) {
		var keyCmp = Key.CompareTo(other.Key);
		if (keyCmp != 0)
			return keyCmp;
		return Position.CompareTo(other.Position);
	}

	public bool Equals(IndexEntryV1 other) {
		return Key == other.Key && Position == other.Position;
	}

	public override string ToString() {
		return string.Format("Key: {0}, Stream: {1}, Version: {2}, Position: {3}", Key, Stream, Version, Position);
	}
}
