// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using Microsoft.Win32.SafeHandles;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

file sealed class SynchronousChunkFileHandle(string path, FileStreamOptions options) : ChunkFileHandle(path, options) {
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> data, long offset, CancellationToken token) {
		var ret = ValueTask.CompletedTask;
		if (token.IsCancellationRequested) {
			ret = ValueTask.FromCanceled(token);
		} else {
			try {
				Write(data.Span, offset);
			} catch (Exception ex) {
				ret = ValueTask.FromException(ex);
			}
		}
		return ret;
	}

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken token) {
		ValueTask<int> ret;
		if (token.IsCancellationRequested) {
			ret = ValueTask.FromCanceled<int>(token);
		} else {
			try {
				ret = new(Read(buffer.Span, offset));
			} catch (Exception ex) {
				ret = ValueTask.FromException<int>(ex);
			}
		}
		return ret;
	}
}

file sealed class AsynchronousChunkFileHandle(string path, FileStreamOptions options) : ChunkFileHandle(path, options) {
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> data, long offset, CancellationToken token) =>
		RandomAccess.WriteAsync(_handle, data, offset, token);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken token) =>
		RandomAccess.ReadAsync(_handle, buffer, offset, token);
}

internal abstract class ChunkFileHandle : Disposable, IChunkHandle {
	public static bool AsynchronousByDefault { get; set; } = false;

	public static FileOptions DefaultFileOptions => AsynchronousByDefault
		? FileOptions.Asynchronous
		: FileOptions.None;

	protected readonly SafeFileHandle _handle;
	private readonly string _path;

	protected ChunkFileHandle(string path, FileStreamOptions options) {
		Debug.Assert(options is not null);
		Debug.Assert(path is { Length: > 0 });

		_handle = File.OpenHandle(path, options.Mode, options.Access, options.Share, options.Options,
			options.PreallocationSize);
		Access = options.Access;
		_path = path;
	}

	public static ChunkFileHandle Create(string path, FileStreamOptions options) =>
		options.Options.HasFlag(FileOptions.Asynchronous)
			? new AsynchronousChunkFileHandle(path, options)
			: new SynchronousChunkFileHandle(path, options);

	internal static FileOptions ConvertToFileOptions(
		IChunkFileSystem.ReadOptimizationHint optimizationHint) {

		var flags = optimizationHint switch {
			IChunkFileSystem.ReadOptimizationHint.RandomAccess => FileOptions.RandomAccess,
			IChunkFileSystem.ReadOptimizationHint.SequentialScan => FileOptions.SequentialScan,
			_ => FileOptions.None,
		};

		return flags | DefaultFileOptions;
	}

	// UnbufferedStreamWithSync makes use of the synchronous Read/Write methods on the handle only
	// when the synchronous Read/Write methods of the stream are called.
	// It is suitable regardless of the value of _asynchronous
	Stream IChunkHandle.CreateStream(bool leaveOpen) => new UnbufferedStreamWithSync(this, leaveOpen);

	private sealed class UnbufferedStreamWithSync(ChunkFileHandle handle, bool leaveOpen)
		: IChunkHandle.UnbufferedStream(handle, leaveOpen) {

		public override bool CanTimeout => false;

		protected override void Write(ReadOnlySpan<byte> buffer, long offset) =>
			handle.Write(buffer, offset);

		protected override int Read(Span<byte> buffer, long offset) =>
			handle.Read(buffer, offset);
	}

	public string Name => _path;

	public void Flush() => RandomAccess.FlushToDisk(_handle);

	public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> data, long offset, CancellationToken token);

	public abstract ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken token);

	protected void Write(ReadOnlySpan<byte> data, long offset) =>
		RandomAccess.Write(_handle, data, offset);

	protected int Read(Span<byte> buffer, long offset) =>
		RandomAccess.Read(_handle, buffer, offset);

	public long Length {
		get => RandomAccess.GetLength(_handle);
		set => RandomAccess.SetLength(_handle, value);
	}

	public FileAccess Access { get; }

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_handle.Dispose();
		}

		base.Dispose(disposing);
	}
}
