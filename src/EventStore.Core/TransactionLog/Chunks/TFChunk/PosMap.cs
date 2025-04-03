// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DotNext.Buffers;
using DotNext.Buffers.Binary;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

[StructLayout(LayoutKind.Auto)]
public readonly struct PosMap : IBinaryFormattable<PosMap> {
	public const int FullSize = sizeof(long) + sizeof(int);
	public const int DeprecatedSize = sizeof(int) + sizeof(int);

	public readonly long LogPos;
	public readonly int ActualPos;

	public PosMap(long logPos, int actualPos) {
		LogPos = logPos;
		ActualPos = actualPos;
	}

	// for new format only
	public PosMap(ReadOnlySpan<byte> source){
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

	public static PosMap FromOldFormat(ReadOnlySpan<byte> source) {
		var posmap = BinaryPrimitives.ReadUInt64LittleEndian(source);
		var logPos = (int)(posmap >>> 32);
		var actualPos = (int)(posmap & 0xFFFFFFFF);
		return new(logPos, actualPos);
	}

	public readonly void Format(Span<byte> destination){
		SpanWriter<byte> writer = new(destination);
		writer.WriteLittleEndian(ActualPos);
		writer.WriteLittleEndian(LogPos);
	}

	public readonly override string ToString() => $"LogPos: {LogPos}, ActualPos: {ActualPos}";
}
