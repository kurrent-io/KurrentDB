// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using DotNext;
using DotNext.Buffers;
using DotNext.IO;
using EventStore.Plugins.Transforms;
using static DotNext.Runtime.Intrinsics;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

internal sealed class ReaderWorkItem : Disposable {
	private const int BufferSize = 8192;

	// if item was taken from the pool, the field contains position within the array (>= 0)
	private readonly int _positionInPool = -1;
	public readonly ChunkDataReadStream BaseStream;
	private readonly bool _leaveOpen;
	private readonly IBufferedReader _cachedReader;

	private ReaderWorkItem(ChunkDataReadStream stream, bool leaveOpen) {
		Debug.Assert(stream is not null);

		_leaveOpen = leaveOpen;
		BaseStream = stream;

		// Access to the internal buffer of 'PoolingBufferedStream' is only allowed
		// when the top-level stream doesn't perform any transformations. Otherwise,
		// the buffer contains untransformed bytes that cannot be accessed directly.
		_cachedReader = IsExactTypeOf<ChunkDataReadStream>(stream)
		                && stream.ChunkFileStream is PoolingBufferedStream bufferedStream
			? bufferedStream
			: null;
	}

	public ReaderWorkItem(Stream sharedStream, IChunkReadTransform chunkReadTransform)
		: this(CreateTransformedMemoryStream(sharedStream, chunkReadTransform), leaveOpen: true) {
		IsMemory = true;
		Source = ITransactionFileTracker.Source.ChunkCache;
	}

	public ReaderWorkItem(IChunkHandle handle, IChunkReadTransform chunkReadTransform, bool isRemoteChunk)
		: this(CreateTransformedFileStream(handle, chunkReadTransform), leaveOpen: false) {
		IsMemory = false;
		Source = isRemoteChunk
			? ITransactionFileTracker.Source.Archive
			: ITransactionFileTracker.Source.FileSystem;
	}

	private static ChunkDataReadStream CreateTransformedMemoryStream(Stream memStream, IChunkReadTransform chunkReadTransform) {
		return chunkReadTransform.TransformData(new ChunkDataReadStream(memStream));
	}

	private static ChunkDataReadStream CreateTransformedFileStream(IChunkHandle handle,
		IChunkReadTransform chunkReadTransform) {
		var fileStream = new PoolingBufferedStream(handle.CreateStream()) { MaxBufferSize = BufferSize };
		return chunkReadTransform.TransformData(new ChunkDataReadStream(fileStream));
	}

	public bool IsMemory { get; }

	public ITransactionFileTracker.Source Source { get; }

	public int PositionInPool {
		get => _positionInPool;
		init {
			Debug.Assert(value >= 0);

			_positionInPool = value;
		}
	}

	internal IBufferedReader TryGetBufferedReader(int length, out ReadOnlyMemory<byte> buffer) {
		if (_cachedReader is { } reader) {
			buffer = reader.Buffer.TrimLength(length);
		} else {
			buffer = ReadOnlyMemory<byte>.Empty;
			reader = null;
		}

		return buffer.Length >= length ? reader : null;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			if (!_leaveOpen)
				BaseStream.Dispose();
		}

		base.Dispose(disposing);
	}
}
