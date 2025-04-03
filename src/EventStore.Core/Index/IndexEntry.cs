// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;

namespace EventStore.Core.Index;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct IndexEntry : IComparable<IndexEntry>, IEquatable<IndexEntry> {
	[FieldOffset(0)] public fixed byte Bytes [24];
	[FieldOffset(0)] public Int64 Version;
	[FieldOffset(8)] public UInt64 Stream;
	[FieldOffset(16)] public Int64 Position;

	public IndexEntry(ulong stream, long version, long position) : this() {
		Stream = stream;
		Version = version;
		Position = position;
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
}
