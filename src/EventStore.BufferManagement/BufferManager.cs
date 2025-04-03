// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace EventStore.BufferManagement;

/// <summary>
/// A manager to handle buffers for the socket connections
/// </summary>
/// <remarks>
/// When used in an async call a buffer is pinned. Large numbers of pinned buffers
/// cause problem with the GC (in particular it causes heap fragmentation).
/// This class maintains a set of large segments and gives clients pieces of these
/// segments that they can use for their buffers. The alternative to this would be to
/// create many small arrays which it then maintained. This methodology should be slightly
/// better than the many small array methodology because in creating only a few very
/// large objects it will force these objects to be placed on the LOH. Since the
/// objects are on the LOH they are at this time not subject to compacting which would
/// require an update of all GC roots as would be the case with lots of smaller arrays
/// that were in the normal heap.
/// </remarks>
public class BufferManager {
        private static readonly ILogger Logger      = Log.ForContext<BufferManager>();

        private const int TrialsCount = 100;

	private static BufferManager _defaultBufferManager;

	private readonly int _segmentChunks;
	private readonly int _chunkSize;
	private readonly int _segmentSize;
	private readonly bool _allowedToCreateMemory;

	private readonly ConcurrentStack<ArraySegment<byte>> _buffers = new ConcurrentStack<ArraySegment<byte>>();

	private readonly List<byte[]> _segments;
	private readonly object _creatingNewSegmentLock = new object();
	

	/// <summary>
	/// Gets the default buffer manager
	/// </summary>
	/// <remarks>You should only be using this method if you don't want to manage buffers on your own.</remarks>
	/// <value>The default buffer manager.</value>
	public static BufferManager Default {
		get {
			//default to 1024 1kb buffers if people don't want to manage it on their own;
			if (_defaultBufferManager == null)
				_defaultBufferManager = new BufferManager(1024, 1024, 1);
			return _defaultBufferManager;
		}
	}

	/// <summary>
	/// Sets the default buffer manager.
	/// </summary>
	/// <param name="manager">The new default buffer manager.</param>
	public static void SetDefaultBufferManager(BufferManager manager) {
		if (manager == null)
			throw new ArgumentNullException("manager");
		_defaultBufferManager = manager;
	}

	public int ChunkSize {
		get { return _chunkSize; }
	}

	public int SegmentsCount {
		get { return _segments.Count; }
	}

	public int SegmentChunksCount {
		get { return _segmentChunks; }
	}

	/// <summary>
	/// The current number of buffers available
	/// </summary>
	public int AvailableBuffers {
		get { return _buffers.Count; } //do we really care about volatility here?
	}

	/// <summary>
	/// The total size of all buffers
	/// </summary>
	public int TotalBufferSize {
		get { return _segments.Count * _segmentSize; } //do we really care about volatility here?
	}

	/// <summary>
	/// Constructs a new <see cref="BufferManager"></see> object
	/// </summary>
	/// <param name="segmentChunks">The number of chunks to create per segment</param>
	/// <param name="chunkSize">The size of a chunk in bytes</param>
	public BufferManager(int segmentChunks, int chunkSize)
		: this(segmentChunks, chunkSize, 1) {
	}

	/// <summary>
	/// Constructs a new <see cref="BufferManager"></see> object
	/// </summary>
	/// <param name="segmentChunks">The number of chunks to create per segment</param>
	/// <param name="chunkSize">The size of a chunk in bytes</param>
	/// <param name="initialSegments">The initial number of segments to create</param>
	public BufferManager(int segmentChunks, int chunkSize, int initialSegments)
		: this(segmentChunks, chunkSize, initialSegments, true) {
	}

	/// <summary>
	/// Constructs a new <see cref="BufferManager"></see> object
	/// </summary>
	/// <param name="segmentChunks">The number of chunks to create per segment</param>
	/// <param name="chunkSize">The size of a chunk in bytes</param>
	/// <param name="initialSegments">The initial number of segments to create</param>
	/// <param name="allowedToCreateMemory">If false when empty and checkout is called an exception will be thrown</param>
	public BufferManager(int segmentChunks, int chunkSize, int initialSegments, bool allowedToCreateMemory) {
		if (segmentChunks <= 0)
			throw new ArgumentException("segmentChunks");
		if (chunkSize <= 0)
			throw new ArgumentException("chunkSize");
		if (initialSegments < 0)
			throw new ArgumentException("initialSegments");

		_segmentChunks = segmentChunks;
		_chunkSize = chunkSize;
		_segmentSize = _segmentChunks * _chunkSize;

		_segments = new List<byte[]>();

		_allowedToCreateMemory = true;
		for (int i = 0; i < initialSegments; i++) {
			CreateNewSegment(true);
		}

		_allowedToCreateMemory = allowedToCreateMemory;
	}

	/// <summary>
	/// Creates a new segment, makes buffers available
	/// </summary>
	private void CreateNewSegment(bool forceCreation) {
		if (!_allowedToCreateMemory)
			throw new UnableToCreateMemoryException();
		lock (_creatingNewSegmentLock) {
			if (!forceCreation && _buffers.Count > _segmentChunks / 2)
				return;

			var bytes = new byte[_segmentSize];
			_segments.Add(bytes);
			for (int i = 0; i < _segmentChunks; i++) {
				var chunk = new ArraySegment<byte>(bytes, i * _chunkSize, _chunkSize);
				_buffers.Push(chunk);
			}

			Logger.Debug(
                    "Segments count: {segments}, buffers count: {buffers}, should be when full: {full}",
				_segments.Count, _buffers.Count, _segments.Count * _segmentChunks
                );
		}
	}

	/// <summary>
	/// Checks out a buffer from the manager
	/// </summary>
	/// <remarks>
	/// It is the client's responsibility to return the buffer to the manager by
	/// calling <see cref="CheckIn(ArraySegment{byte})"></see> on the buffer
	/// </remarks>
	/// <returns>A <see cref="ArraySegment{T}"></see> that can be used as a buffer</returns>
	public ArraySegment<byte> CheckOut() {
		int trial = 0;
		while (trial < TrialsCount) {
			ArraySegment<byte> result;
			if (_buffers.TryPop(out result))
				return result;
			CreateNewSegment(false);
			trial++;
		}

		throw new UnableToAllocateBufferException();
	}

	/// <summary>
	/// Checks out a buffer from the manager
	/// </summary>
	/// <remarks>
	/// It is the client's responsibility to return the buffer to the manger by
	/// calling <see cref="CheckIn(IEnumerable{ArraySegment{byte}})"></see> on the buffer
	/// </remarks>
	/// <returns>A <see cref="ArraySegment{T}"></see> that can be used as a buffer</returns>
	public IEnumerable<ArraySegment<byte>> CheckOut(int toGet) {
		var result = new ArraySegment<byte>[toGet];
		var count = 0;
		var totalReceived = 0;

		try {
			while (count < TrialsCount) {
				ArraySegment<byte> piece;
				while (totalReceived < toGet) {
					if (!_buffers.TryPop(out piece))
						break;
					result[totalReceived] = piece;
					++totalReceived;
				}

				if (totalReceived == toGet)
					return result;
				CreateNewSegment(false);
				count++;
			}

			throw new UnableToAllocateBufferException();
		} catch {
			if (totalReceived > 0)
				CheckIn(result.Take(totalReceived));
			throw;
		}
	}

	/// <summary>
	/// Returns a buffer to the control of the manager
	/// </summary>
	/// <remarks>
	/// It is the client's responsibility to return the buffer to the manger by
	/// calling <see cref="CheckIn(ArraySegment{byte})"></see> on the buffer
	/// </remarks>
	/// <param name="buffer">The <see cref="ArraySegment{T}"></see> to return to the cache</param>
	public void CheckIn(ArraySegment<byte> buffer) {
		CheckBuffer(buffer);
		_buffers.Push(buffer);
	}

	/// <summary>
	/// Returns a set of buffers to the control of the manager
	/// </summary>
	/// <remarks>
	/// It is the client's responsibility to return the buffer to the manger by
	/// calling <see cref="CheckIn(IEnumerable{ArraySegment{byte}})"></see> on the buffer
	/// </remarks>
	/// <param name="buffersToReturn">The <see cref="ArraySegment{T}"></see> to return to the cache</param>
	public void CheckIn(IEnumerable<ArraySegment<byte>> buffersToReturn) {
		if (buffersToReturn == null)
			throw new ArgumentNullException("buffersToReturn");

		foreach (var buf in buffersToReturn) {
			CheckBuffer(buf);
			_buffers.Push(buf);
		}
	}

	//[Conditional("DEBUG")]
	private void CheckBuffer(ArraySegment<byte> buffer) {
		if (buffer.Array == null || buffer.Count == 0 || buffer.Array.Length < buffer.Offset + buffer.Count)
			throw new Exception("Attempt to check in invalid buffer");
		if (buffer.Count != _chunkSize)
			throw new ArgumentException("Buffer was not of the same chunk size as the buffer manager", "buffer");
	}
}
