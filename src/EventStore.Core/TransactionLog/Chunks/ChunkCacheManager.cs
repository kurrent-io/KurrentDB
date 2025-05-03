using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Serilog;

namespace EventStore.Core.TransactionLog.Chunks;

public interface IChunkCacheManager {
	public nint AllocateAtLeast(int numBytes);
	public void Free(nint pointer);
}

public class UnmanagedChunkCacheManager : IChunkCacheManager {
	private readonly object _lock = new();
	
	// the actual lengths of all the buffers
	private readonly Dictionary<nint, int> _bufferLengths = [];

	public nint AllocateAtLeast(int numBytes) {
		lock (_lock) {
			var pointer = Marshal.AllocHGlobal(numBytes);
			GC.AddMemoryPressure(numBytes);
			_bufferLengths[pointer] = numBytes;
			return pointer;
		}
	}

	public void Free(nint pointer) {
		lock (_lock) {
			if (!_bufferLengths.Remove(pointer, out var bufferLength))
				throw new InvalidOperationException("Attempted to free unknown buffer");

			Marshal.FreeHGlobal(pointer);
			GC.RemoveMemoryPressure(bufferLength);
		}
	}
}

// This naive POC allocates whatever it needs and doesn't free anything.
public class PoolingChunkCacheManager : IChunkCacheManager {
	private static readonly ILogger Log = Serilog.Log.ForContext<PoolingChunkCacheManager>();

	private readonly object _lock = new();
	private readonly IChunkCacheManager _inner;
	private readonly int _minBufferSize;
	private readonly bool _cleanBuffers;
	private readonly Dictionary<nint, int> _bufferLengths = []; // the actual lengths of all the buffers
	private readonly Stack<nint> _freeBuffers = [];

	public PoolingChunkCacheManager(
		IChunkCacheManager inner,
		int minBufferSize,
		bool cleanBuffers,
		int initialBuffers) {

		_inner = inner;
		_minBufferSize = minBufferSize;
		_cleanBuffers = cleanBuffers;

		var buffers = new nint[initialBuffers];

		for (var i = 0; i < initialBuffers; i++)
			buffers[i] = AllocateAtLeast(minBufferSize);

		for (var i = 0; i < initialBuffers; i++)
			Free(buffers[i]);
	}

	// This reuses a free buffer if it can, otherwise allocates a new one.
	public nint AllocateAtLeast(int requestedBytes) {
		lock (_lock) {
			var reason = "";

			if (_freeBuffers.TryPeek(out var peeked)) {
				if (_bufferLengths[peeked] >= requestedBytes) {
					// found a buffer we can use.
					_freeBuffers.Pop();
					Log.Information($"#### Saved allocating a buffer for the chunk cache by reusing");
					return peeked;

				} else {
					reason = $"available buffer was too small ({_bufferLengths[peeked]:N0})";
				}

			} else {
				reason = $"no buffers are available";
			}

			var actualBytesToAllocate = Math.Max(_minBufferSize, requestedBytes);
			Log.Warning($"#### Allocating a new buffer of size {actualBytesToAllocate:N0} bytes for the chunk cache because {reason}");
			var p = _inner.AllocateAtLeast(actualBytesToAllocate);
			_bufferLengths[p] = actualBytesToAllocate;
			return p;
		}
	}

	unsafe public void Free(nint pointer) {
		lock (_lock) {
			if (!_bufferLengths.TryGetValue(pointer, out var bufferLength))
				throw new InvalidOperationException("Attempted to free unknown buffer");

			Log.Information($"#### Returned a chunk cache buffer to the pool");

			if (_cleanBuffers) {
				var span = new Span<byte>((void*)pointer, bufferLength);
				span.Clear();
			}

			_freeBuffers.Push(pointer);
		}
	}
}
