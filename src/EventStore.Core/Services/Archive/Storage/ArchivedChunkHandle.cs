// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext.IO;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.Services.Archive.Storage;

public sealed class ArchivedChunkHandle : IChunkHandle {
	private readonly IArchiveStorageReader _reader;
	private readonly int _logicalChunkNumber;
	private readonly long _length;

	private ArchivedChunkHandle(IArchiveStorageReader reader, int logicalChunkNumber, long length) {
		Debug.Assert(reader is not null);
		Debug.Assert(length >= 0L);

		_reader = reader;
		_logicalChunkNumber = logicalChunkNumber;
		_length = length;
	}

	public static async ValueTask<IChunkHandle> OpenForReadAsync(IArchiveStorageReader reader, int logicalChunkNumber, CancellationToken token) {
		var metadata = await reader.GetMetadataAsync(logicalChunkNumber, token);
		return new ArchivedChunkHandle(reader, logicalChunkNumber, metadata.PhysicalSize);
	}

	void IFlushable.Flush() {
		// nothing to flush
	}

	Task IFlushable.FlushAsync(CancellationToken token) => CompletedOrCanceled(token);

	ValueTask IChunkHandle.WriteAsync(ReadOnlyMemory<byte> data, long offset, CancellationToken token)
		=> ValueTask.FromException(new NotSupportedException());

	public ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken token)
		=> offset < _length
			? _reader.ReadAsync(_logicalChunkNumber, buffer, offset, token)
			: ValueTask.FromResult(0); // _reader.ReadAsync will give this behaviour too, but we can do it here more cheaply

	public string Name => $"archive-handle-{_logicalChunkNumber}";

	public long Length {
		get => _length;
		set => throw new NotSupportedException();
	}

	public FileAccess Access { get; init; } = FileAccess.Read;

	private static Task CompletedOrCanceled(CancellationToken token)
		=> token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;

	void IDisposable.Dispose() {
		// nothing to dispose
	}
}
