// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using DotNext.Buffers;
using DotNext.Buffers.Binary;
using DotNext.IO;
using EventStore.Plugins.Transforms;
using ChunkVersions = EventStore.Core.TransactionLog.Chunks.TFChunk.TFChunk.ChunkVersions;

namespace EventStore.Core.TransactionLog.Chunks;


// TODO: Consider struct instead of class
public sealed class ChunkHeader : IBinaryFormattable<ChunkHeader> {
	public const int Size = TFConsts.ChunkHeaderSize;

	public readonly long ChunkStartPosition; // return ChunkStartNumber * (long)ChunkSize;
	public readonly long ChunkEndPosition; // return (ChunkEndNumber + 1) * (long)ChunkSize;

	public readonly byte Version;
	public readonly byte MinCompatibleVersion; // the minimum version of the chunk format that the db needs to "understand" to be able to read/write to this chunk
	public readonly int ChunkSize;
	public readonly int ChunkStartNumber;
	public readonly int ChunkEndNumber;
	public readonly bool IsScavenged; // uses 4 bytes (legacy)
	public readonly Guid ChunkId;
	public readonly TransformType TransformType;

	public ChunkHeader(byte version, byte minCompatibleVersion, int chunkSize, int chunkStartNumber, int chunkEndNumber, bool isScavenged,
		Guid chunkId, TransformType transformType) {
		Ensure.Nonnegative(version, "version");
		Ensure.Nonnegative(minCompatibleVersion, "minCompatibleVersion");
		Ensure.Positive(chunkSize, "chunkSize");
		Ensure.Nonnegative(chunkStartNumber, "chunkStartNumber");
		Ensure.Nonnegative(chunkEndNumber, "chunkEndNumber");
		if (chunkStartNumber > chunkEndNumber)
			throw new ArgumentOutOfRangeException("chunkStartNumber",
				"chunkStartNumber is greater than ChunkEndNumber.");

		Version = version;
		MinCompatibleVersion = minCompatibleVersion;
		ChunkSize = chunkSize;
		ChunkStartNumber = chunkStartNumber;
		ChunkEndNumber = chunkEndNumber;
		IsScavenged = isScavenged;
		ChunkId = chunkId;
		TransformType = transformType;

		ChunkStartPosition = ChunkStartNumber * (long)ChunkSize;
		ChunkEndPosition = (ChunkEndNumber + 1) * (long)ChunkSize;
	}

	public ChunkHeader(ReadOnlySpan<byte> source) {
		Debug.Assert(source.Length >= Size);

		SpanReader<byte> reader = new(source.Slice(0, Size));

		if ((FileType)reader.Read() is not FileType.ChunkFile)
			throw new CorruptDatabaseException(new InvalidFileException());

		MinCompatibleVersion = reader.Read();

		ChunkSize = reader.ReadLittleEndian<int>();
		Debug.Assert(ChunkSize >= 0);

		ChunkStartNumber = reader.ReadLittleEndian<int>();
		Debug.Assert(ChunkStartNumber >= 0);

		ChunkEndNumber = reader.ReadLittleEndian<int>();
		Debug.Assert(ChunkEndNumber >= 0);

		IsScavenged = reader.ReadLittleEndian<int>() > 0;
		ChunkId = new(reader.Read(16));

		Version = reader.Read();

		if (Version is 0)
			Version = MinCompatibleVersion;
		Debug.Assert(Version >= MinCompatibleVersion);

		if (Version >= (byte)ChunkVersions.Transformed)
			TransformType = (TransformType) reader.Read();
		else
			TransformType = TransformType.Identity;

		ChunkStartPosition = ChunkStartNumber * (long)ChunkSize;
		ChunkEndPosition = (ChunkEndNumber + 1) * (long)ChunkSize;
	}

	static int IBinaryFormattable<ChunkHeader>.Size => Size;

	public void Format(Span<byte> destination) {
		Debug.Assert(destination.Length >= Size);

		SpanWriter<byte> writer = new(destination);
		writer.Add((byte)FileType.ChunkFile);
		writer.Add(MinCompatibleVersion);
		writer.WriteLittleEndian(ChunkSize);
		writer.WriteLittleEndian(ChunkStartNumber);
		writer.WriteLittleEndian(ChunkEndNumber);
		writer.WriteLittleEndian<int>(Unsafe.BitCast<bool, byte>(IsScavenged));
		ChunkId.TryWriteBytes(writer.Slide(16));

		if (Version >= (byte)ChunkVersions.Transformed)
			// we started to use this byte of the chunk header to store `Version` as from `ChunkVersions.Transformed`
			// so, we don't write it for older versions to keep the previous formats the same.
			writer.Add(Version);

		if (Version >= (byte)ChunkVersions.Transformed)
			writer.Add((byte)TransformType);

		// reserved bytes must be zero
		writer.RemainingSpan.Clear();
	}

	static ChunkHeader IBinaryFormattable<ChunkHeader>.Parse(ReadOnlySpan<byte> source)
		=> new(source);

	public byte[] AsByteArray() {
		var array = new byte[Size];
		Format(array);

		return array;
	}

	public static async ValueTask<ChunkHeader> FromStream(Stream stream, CancellationToken token) {
		using var buffer = Memory.AllocateExactly<byte>(Size);
		return await stream.ReadAsync<ChunkHeader>(buffer.Memory, token);
	}

	public long GetLocalLogPosition(long globalLogicalPosition) {
		if (globalLogicalPosition < ChunkStartPosition || globalLogicalPosition > ChunkEndPosition) {
			throw new Exception(
				$"globalLogicalPosition {globalLogicalPosition} is out of chunk logical positions [{ChunkStartPosition}, {ChunkEndPosition}].");
		}

		return globalLogicalPosition - ChunkStartPosition;
	}

	public long GetGlobalLogPosition(long localLogicalPosition) {
		if (ChunkStartPosition + localLogicalPosition > ChunkEndPosition) {
			throw new Exception(
				$"localLogicalPosition {localLogicalPosition} is out of chunk logical positions [{ChunkStartPosition}, {ChunkEndPosition}].");
		}

		return ChunkStartPosition + localLogicalPosition;
	}

	public bool IsSingleLogicalChunk => ChunkStartNumber == ChunkEndNumber;

	public override string ToString() {
		return string.Format(
			"Version: {0}, ChunkSize: {1}, ChunkStartNumber: {2}, ChunkEndNumber: {3}, IsScavenged: {4}, ChunkId: {5}\n" +
			"TransformType: {6}, ChunkStartPosition: {7}, ChunkEndPosition: {8}, ChunkFullSize: {9}",
			Version,
			ChunkSize,
			ChunkStartNumber,
			ChunkEndNumber,
			IsScavenged,
			ChunkId,
			TransformType,
			ChunkStartPosition,
			ChunkEndPosition,
			ChunkEndPosition - ChunkStartPosition);
	}
}
