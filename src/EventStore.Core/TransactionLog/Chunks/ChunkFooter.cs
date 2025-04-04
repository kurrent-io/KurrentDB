// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using DotNext.Buffers;
using DotNext.Buffers.Binary;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Chunks;


// TODO: Consider struct instead of class
public sealed class ChunkFooter : IBinaryFormattable<ChunkFooter> {
	public const int Size = TFConsts.ChunkFooterSize;
	public const int ChecksumSize = 16;

	// flags within single byte
	public readonly bool IsCompleted;
	public readonly bool IsMap12Bytes;

	public readonly int PhysicalDataSize; // the size of a section of data in chunk

	public readonly long
		LogicalDataSize; // the size of a logical data size (after scavenge LogicalDataSize can be > physicalDataSize)

	public readonly int MapSize;
	public readonly byte[] MD5Hash; // TODO: Allocation can be removed with InlineArray

	public readonly int MapCount; // calculated, not stored

	public ChunkFooter(bool isCompleted, bool isMap12Bytes, int physicalDataSize, long logicalDataSize, int mapSize,
		byte[] md5Hash) {
		Ensure.Nonnegative(physicalDataSize, "physicalDataSize");
		Ensure.Nonnegative(logicalDataSize, "logicalDataSize");
		if (logicalDataSize < physicalDataSize)
			throw new ArgumentOutOfRangeException("logicalDataSize",
				string.Format("LogicalDataSize {0} is less than PhysicalDataSize {1}", logicalDataSize,
					physicalDataSize));
		Ensure.Nonnegative(mapSize, "mapSize");
		Ensure.NotNull(md5Hash, "md5Hash");
		if (md5Hash.Length != ChecksumSize)
			throw new ArgumentException("MD5Hash is of wrong length.", "md5Hash");

		IsCompleted = isCompleted;
		IsMap12Bytes = isMap12Bytes;

		PhysicalDataSize = physicalDataSize;
		LogicalDataSize = logicalDataSize;
		MapSize = mapSize;
		MD5Hash = md5Hash;

		var posMapSize = isMap12Bytes ? PosMap.FullSize : PosMap.DeprecatedSize;
		if (MapSize % posMapSize != 0)
			throw new Exception(string.Format("Wrong MapSize {0} -- not divisible by PosMap.Size {1}.", MapSize,
				posMapSize));
		MapCount = mapSize / posMapSize;
	}

	public ChunkFooter(ReadOnlySpan<byte> source) {
		Debug.Assert(source.Length >= Size);

		SpanReader<byte> reader = new(source);
		byte flags = reader.Read();

		IsCompleted = (flags & 1) is not 0;
		IsMap12Bytes = (flags & 2) is not 0;
		PhysicalDataSize = reader.ReadLittleEndian<int>();
		LogicalDataSize = IsMap12Bytes
			? reader.ReadLittleEndian<long>()
			: reader.ReadLittleEndian<int>();

		MapSize = reader.ReadLittleEndian<int>();
		reader.ConsumedCount = Size - ChecksumSize;
		MD5Hash = reader.ReadToEnd().ToArray();

		var posMapSize = IsMap12Bytes ? PosMap.FullSize : PosMap.DeprecatedSize;
		if (MapSize % posMapSize is not 0) {
			throw new Exception(string.Format("Wrong MapSize {0} -- not divisible by PosMap.Size {1}.", MapSize,
				posMapSize));
		}

		MapCount = MapSize / posMapSize;
	}

	static int IBinaryFormattable<ChunkFooter>.Size => Size;

	public void Format(Span<byte> destination) {
		Debug.Assert(destination.Length >= Size);

		SpanWriter<byte> writer = new(destination);
		int flags = Unsafe.BitCast<bool, byte>(IsCompleted)
			| Unsafe.BitCast<bool, byte>(IsMap12Bytes) << 1;

		writer.Add((byte)flags);
		writer.WriteLittleEndian(PhysicalDataSize);
		if (IsMap12Bytes)
			writer.WriteLittleEndian(LogicalDataSize);
		else
			writer.WriteLittleEndian((int)LogicalDataSize);

		writer.WriteLittleEndian(MapSize);
		writer.WrittenCount = Size - ChecksumSize;
		writer.Write(MD5Hash);
	}

	static ChunkFooter IBinaryFormattable<ChunkFooter>.Parse(ReadOnlySpan<byte> source)
		=> new(source);

	public byte[] AsByteArray() {
		var array = new byte[Size];
		Format(array);
		return array;
	}

	[SkipLocalsInit]
	public static ChunkFooter FromStream(Stream stream) {
		Span<byte> buffer = stackalloc byte[Size];
		stream.ReadExactly(buffer);
		return new(buffer);
	}
}
