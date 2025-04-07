// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using DotNext.Buffers;
using DotNext.Buffers.Binary;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

public struct PosMap : IBinaryFormattable<PosMap> {
	public const int FullSize = sizeof(long) + sizeof(int);
	public const int DeprecatedSize = sizeof(int) + sizeof(int);

	public readonly long LogPos;
	public readonly int ActualPos;

	public PosMap(long logPos, int actualPos) {
		LogPos = logPos;
		ActualPos = actualPos;
	}

	// for new format only
	public PosMap(ReadOnlySpan<byte> source) {
		Debug.Assert(source.Length >= FullSize);

		SpanReader<byte> reader = new(source);
		ActualPos = reader.ReadLittleEndian<int>();
		LogPos = reader.ReadLittleEndian<long>();
	}

	static int IBinaryFormattable<PosMap>.Size => FullSize;

	static PosMap IBinaryFormattable<PosMap>.Parse(ReadOnlySpan<byte> source)
		=> FromNewFormat(source);

	public static PosMap FromNewFormat(ReadOnlySpan<byte> source)
		=> new(source);

	public static PosMap FromNewFormat(BinaryReader reader) {
		var actualPos = reader.ReadInt32();
		var logPos = reader.ReadInt64();
		return new PosMap(logPos, actualPos);
	}

	public static PosMap FromOldFormat(ReadOnlySpan<byte> source) {
		SpanReader<byte> reader = new(source);
		var posmap = reader.ReadLittleEndian<ulong>();
		var logPos = (int)(posmap >>> 32);
		var actualPos = (int)(posmap & 0xFFFFFFFF);
		return new(logPos, actualPos);
	}

	public static PosMap FromOldFormat(BinaryReader reader) {
		Span<byte> buffer = stackalloc byte[DeprecatedSize];
		var bytesRead = reader.Read(buffer);
		return FromOldFormat(buffer.Slice(0, bytesRead));
	}

	public readonly void Write(BinaryWriter writer) {
		Span<byte> buffer = stackalloc byte[FullSize];
		Format(buffer);
		writer.Write(buffer);
	}

	public readonly void Format(Span<byte> destination) {
		SpanWriter<byte> writer = new(destination);
		writer.WriteLittleEndian(ActualPos);
		writer.WriteLittleEndian(LogPos);
	}

	public readonly override string ToString() {
		return string.Format("LogPos: {0}, ActualPos: {1}", LogPos, ActualPos);
	}
}
