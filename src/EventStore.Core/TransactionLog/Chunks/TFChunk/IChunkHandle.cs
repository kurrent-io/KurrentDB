// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using DotNext.Buffers;
using DotNext.IO;
using DotNext.Threading.Tasks;
using Serilog;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

/// <summary>
/// Represents a handle to access the underlying chunk physical storage.
/// </summary>
public interface IChunkHandle : IFlushable, IDisposable {

	ValueTask WriteAsync(ReadOnlyMemory<byte> data, long offset, CancellationToken token);

	ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken token);

	/// <summary>
	/// Gets or sets the length of the data represented by this handle, in bytes.
	/// </summary>
	long Length {
		get;
		set;
	}

	string Name { get; }

	/// <summary>
	/// Gets access mode for this handle.
	/// </summary>
	FileAccess Access { get; }

	/// <summary>
	/// Creates an unbuffered stream for this handle.
	/// </summary>
	/// <returns>The unbuffered stream for this handle.</returns>
	Stream CreateStream(bool leaveOpen = true) => CreateStream(this, leaveOpen, 60_000);

	protected static Stream CreateStream(IChunkHandle handle, bool leaveOpen, int synchronousTimeout)
		=> new UnbufferedStream(handle, leaveOpen) { ReadTimeout = synchronousTimeout, WriteTimeout = synchronousTimeout };

	private protected class UnbufferedStream(IChunkHandle handle, bool leaveOpen) : RandomAccessStream {
		private int _readTimeout, _writeTimeout;
		private CancellationTokenSource _timeoutSource;

		public sealed override void Flush() => handle.Flush();

		public sealed override void SetLength(long value) => handle.Length = value;

		public sealed override bool CanRead => handle.Access.HasFlag(FileAccess.Read);

		public sealed override bool CanSeek => true;

		public sealed override bool CanWrite => handle.Access.HasFlag(FileAccess.Write);

		public override bool CanTimeout => true;

		public sealed override int WriteTimeout {
			get => _writeTimeout;
			set => _writeTimeout =
				value >= Timeout.Infinite ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}

		public sealed override int ReadTimeout {
			get => _readTimeout;
			set => _readTimeout =
				value >= Timeout.Infinite ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}

		public sealed override long Length => handle.Length;

		protected override void Write(ReadOnlySpan<byte> buffer, long offset) {
			// leave fast without sync over async
			if (buffer.IsEmpty)
				return;

			// Do sync over async without any optimizations to make it just works.
			// In practice, no one should call synchronous write
			var bufferCopy = buffer.Copy();
			var timeoutToken = GetTimeoutToken(WriteTimeout);
			var task = WriteAsync(bufferCopy.Memory, offset, timeoutToken);
			try {
				task.Wait();
			} catch (OperationCanceledException canceledEx) when (canceledEx.CancellationToken == timeoutToken) {
				throw new TimeoutException(canceledEx.Message, canceledEx);
			} finally {
				ResetTimeout();
				bufferCopy.Dispose();
			}

			// This synchronous Write implementation exists because it is hard to be sure that it is
			// never called. Quite a few synchronous stream operations can call synchronous write under
			// the hood (e.g. SetLength). We want to be sure that these are at least not called
			// routinely, because it is inefficient.
			Log.Warning("Synchronous writes should be uncommon. Handle: {Handle}", handle.Name);
		}


		protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, long offset, CancellationToken token)
			=> handle.WriteAsync(buffer, offset, token);

		protected override int Read(Span<byte> buffer, long offset) {
			// leave fast without sync over async
			int bytesRead;
			if (buffer.IsEmpty) {
				bytesRead = 0;
			} else {
				// Do sync over async without any optimizations to make it just works.
				// In practice, no one should call synchronous write
				var bufferCopy = Memory.AllocateExactly<byte>(buffer.Length);
				var timeoutToken = GetTimeoutToken(ReadTimeout);
				var task = ReadAsync(bufferCopy.Memory, offset, timeoutToken);
				try {
					bytesRead = task.Wait();
					bufferCopy.Span.Slice(0, bytesRead).CopyTo(buffer);
				} catch (OperationCanceledException canceledEx) when (canceledEx.CancellationToken == timeoutToken) {
					throw new TimeoutException(canceledEx.Message, canceledEx);
				} finally {
					ResetTimeout();
					bufferCopy.Dispose();
				}
			}

			Log.Warning("Synchronous reads should be uncommon. Handle: {Handle}", handle.Name);
			return bytesRead;
		}

		protected override ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken token)
			=> handle.ReadAsync(buffer, offset, token);

		private CancellationToken GetTimeoutToken(int timeout) {
			_timeoutSource ??= new();
			_timeoutSource.CancelAfter(timeout);
			return _timeoutSource.Token;
		}

		private void ResetTimeout() {
			// attempt to reuse the token source to avoid extra memory allocation
			if (!_timeoutSource.TryReset()) {
				_timeoutSource.Dispose();
				_timeoutSource = null;
			}
		}

		protected override void Dispose(bool disposing) {
			if (disposing) {
				_timeoutSource?.Dispose();
				if (!leaveOpen)
					handle.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
