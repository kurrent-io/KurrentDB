// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Runtime.InteropServices;

namespace KurrentDB.Core.Index;

public readonly struct IndexEntry : IComparable<IndexEntry>, IEquatable<IndexEntry> {
	public readonly Int64 Version;
	public readonly UInt64 Stream;
	public readonly Int64 Position;

	public PTable.IndexEntryKey Key => new(Stream, Version);

	public IndexEntry(ulong stream, long version, long position) : this() {
		Stream = stream;
		Version = version;
		Position = position;
	}

	public void AppendTo<T>(Stream stream) where T : struct, ILayout<T> {
		T.AppendTo(stream, in this);
	}

	[SkipLocalsInit]
	public static IndexEntry ReadFrom<T>(Stream stream) where T : struct, ILayout<T> {
		Span<byte> buffer = stackalloc byte[T.Size];
		stream.ReadExactly(buffer);
		return T.ReadFrom(buffer);
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
		return $"Stream: {Stream}, Version: {Version}, Position: {Position}";
	}

	[StructLayout(LayoutKind.Explicit)]
	public readonly struct V3(ulong stream, long version, long position) : ILayout<V3> {
		[FieldOffset(0)] public readonly Int64 Version = version;
		[FieldOffset(8)] public readonly UInt64 Stream = stream;
		[FieldOffset(16)] public readonly Int64 Position = position;

		public static int Size => 24;

		public static IndexEntry ReadFrom(ReadOnlySpan<byte> buffer) {
			Debug.Assert(Unsafe.SizeOf<V3>() == Size);
			ref readonly var entry = ref MemoryMarshal.AsRef<V3>(buffer);
			return new IndexEntry(entry.Stream, entry.Version, entry.Position);
		}

		public static void AppendTo(Stream stream, in IndexEntry entry) {
			var value = new V3(entry.Stream, entry.Version, entry.Position);
			var buffer = MemoryMarshal.AsReadOnlyBytes(in value);
			Debug.Assert(buffer.Length == Size);
			stream.Write(buffer);
		}
	}

	[StructLayout(LayoutKind.Explicit, Pack = 4)]
	public readonly struct V2(ulong stream, int version, long position) : ILayout<V2> {
		[FieldOffset(0)] public readonly Int32 Version = version;
		[FieldOffset(4)] public readonly UInt64 Stream = stream;
		[FieldOffset(12)] public readonly Int64 Position = position;

		public static int Size => 20;

		public static IndexEntry ReadFrom(ReadOnlySpan<byte> buffer) {
			Debug.Assert(Unsafe.SizeOf<V2>() == Size);
			ref readonly var entry = ref MemoryMarshal.AsRef<V2>(buffer);
			return new IndexEntry(entry.Stream, entry.Version, entry.Position);
		}

		public static void AppendTo(Stream stream, in IndexEntry entry) {
			var value = new V2(entry.Stream, (int)entry.Version, entry.Position);
			var buffer = MemoryMarshal.AsReadOnlyBytes(in value);
			Debug.Assert(buffer.Length == Size);
			stream.Write(buffer);
		}
	}

	[StructLayout(LayoutKind.Explicit)]
	public readonly struct V1(uint stream, int version, long position) : ILayout<V1> {
		[FieldOffset(0)] public readonly Int32 Version = version;
		[FieldOffset(4)] public readonly UInt32 Stream = stream;
		[FieldOffset(8)] public readonly Int64 Position = position;

		public static int Size => 16;

		public static IndexEntry ReadFrom(ReadOnlySpan<byte> buffer) {
			Debug.Assert(Unsafe.SizeOf<V1>() == Size);
			ref readonly var entry = ref MemoryMarshal.AsRef<V1>(buffer);
			return new IndexEntry(entry.Stream, entry.Version, entry.Position);
		}

		public static void AppendTo(Stream stream, in IndexEntry entry) {
			var value = new V1((uint)entry.Stream, (int)entry.Version, entry.Position);
			var buffer = MemoryMarshal.AsReadOnlyBytes(in value);
			Debug.Assert(buffer.Length == Size);
			stream.Write(buffer);
		}
	}

	public interface ILayout<TSelf> where TSelf : struct, ILayout<TSelf> {
		static abstract int Size { get; }
		static abstract IndexEntry ReadFrom(ReadOnlySpan<byte> buffer);
		static abstract void AppendTo(Stream stream, in IndexEntry entry);
	}
}
