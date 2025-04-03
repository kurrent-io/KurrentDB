// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using DotNext.Buffers;
using DotNext.Buffers.Binary;
using DotNext.IO;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.TransactionLog.Chunks;

// TODO: Consider struct instead of class
public sealed class ChunkFooter : IBinaryFormattable<ChunkFooter> {
	public const int Size = TFConsts.ChunkFooterSize;
	public const int ChecksumSize = 16;

	private readonly MD5HashBuffer _checksum;

	// flags within single byte
	public readonly bool IsCompleted;
	public readonly bool IsMap12Bytes;

	public readonly int PhysicalDataSize; // the size of a section of data in chunk

	public readonly long
		LogicalDataSize; // the size of a logical data size (after scavenge LogicalDataSize can be > physicalDataSize)

	public readonly int MapSize;

	public ReadOnlySpan<byte> MD5Hash {
		get => _checksum;
		init => value.CopyTo(_checksum);
	}

	public readonly int MapCount; // calculated, not stored

	public ChunkFooter(bool isCompleted, bool isMap12Bytes, int physicalDataSize, long logicalDataSize, int mapSize, IncrementalHash hash = null) {
		Ensure.Nonnegative(physicalDataSize, nameof(physicalDataSize));
		Ensure.Nonnegative(logicalDataSize, nameof(logicalDataSize));
		if (logicalDataSize < physicalDataSize)
			throw new ArgumentOutOfRangeException(nameof(logicalDataSize),
				$"LogicalDataSize {logicalDataSize} is less than PhysicalDataSize {physicalDataSize}");
		Ensure.Nonnegative(mapSize, "mapSize");

		IsCompleted = isCompleted;
		IsMap12Bytes = isMap12Bytes;

		PhysicalDataSize = physicalDataSize;
		LogicalDataSize = logicalDataSize;
		MapSize = mapSize;

		Debug.Assert(hash is null || hash.HashLengthInBytes is ChecksumSize);
		Unsafe.SkipInit(out _checksum); // fix for Qodana false positive about init of readonly field
		hash?.TryGetHashAndReset(_checksum, out _);

		var posMapSize = isMap12Bytes ? PosMap.FullSize : PosMap.DeprecatedSize;
		if (MapSize % posMapSize is not 0)
			throw new Exception($"Wrong MapSize {MapSize} -- not divisible by PosMap.Size {posMapSize}.");
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
		reader.Read(_checksum);

		var posMapSize = IsMap12Bytes ? PosMap.FullSize : PosMap.DeprecatedSize;
		if (MapSize % posMapSize is not 0) {
			throw new Exception($"Wrong MapSize {MapSize} -- not divisible by PosMap.Size {posMapSize}.");
		}

		MapCount = MapSize / posMapSize;
	}

	static int IBinaryFormattable<ChunkFooter>.Size => Size;

	public void Format(Span<byte> destination) {
		Debug.Assert(destination.Length >= Size);

		SpanWriter<byte> writer = new(destination.Slice(0, Size));
		int flags = Unsafe.BitCast<bool, byte>(IsCompleted)
			| Unsafe.BitCast<bool, byte>(IsMap12Bytes) << 1;

		writer.Add((byte)flags);
		writer.WriteLittleEndian(PhysicalDataSize);
		if (IsMap12Bytes)
			writer.WriteLittleEndian(LogicalDataSize);
		else
			writer.WriteLittleEndian((int)LogicalDataSize);

		writer.WriteLittleEndian(MapSize);

		// reserved bytes must be zero
		writer.Slide(Size - writer.WrittenCount - ChecksumSize).Clear();

		writer.Write(_checksum);
	}

	static ChunkFooter IBinaryFormattable<ChunkFooter>.Parse(ReadOnlySpan<byte> source)
		=> new(source);

	public byte[] AsByteArray() {
		var array = new byte[Size];
		Format(array);
		return array;
	}

	public static async ValueTask<ChunkFooter> FromStream(Stream stream, CancellationToken token) {
		using var buffer = Memory.AllocateExactly<byte>(Size);
		return await stream.ReadAsync<ChunkFooter>(buffer.Memory, token);
	}

	[InlineArray(ChecksumSize)]
	private struct MD5HashBuffer {
		private byte _element0;
	}
}
