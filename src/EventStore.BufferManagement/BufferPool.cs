// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;

namespace EventStore.BufferManagement;

public class BufferPool : IDisposable {
	private List<ArraySegment<byte>> _buffers;
	private readonly BufferManager _bufferManager;
	private readonly int _chunkSize;
	private int _length;
	private bool _disposed;

	/// <summary>
	/// Structure to represent an index and an offset into the list of buffers allowing for quick access to a position
	/// </summary>
	private struct Position {
		public readonly int Index;
		public readonly int Offset;

		public Position(int index, int offset) {
			Index = index;
			Offset = offset;
		}
	}

	/// <summary>
	/// Gets the capacity of the <see cref="BufferPool"></see>
	/// </summary>
	/// <value>The capacity.</value>
	public int Capacity {
		get {
			CheckDisposed();
			return _chunkSize * _buffers.Count;
		}
	}

	/// <summary>
	/// Gets the current length of the <see cref="BufferPool"></see>
	/// </summary>
	/// <value>The length.</value>
	public int Length {
		get { return _length; }
	}

	/// <summary>
	/// Gets the effective buffers contained in this <see cref="BufferPool"></see>
	/// </summary>
	/// <value>The effective buffers.</value>
	public IEnumerable<ArraySegment<byte>> EffectiveBuffers {
		get {
			CheckDisposed();
			if (_length > 0) {
				Position l = GetPositionFor(_length);
				//send full buffers
				for (int i = 0; i < l.Index; i++) {
					yield return _buffers[i];
				}

				//send partial buffer
				ArraySegment<byte> last = _buffers[l.Index];
				yield return new ArraySegment<byte>(last.Array, last.Offset, l.Offset);
			}
		}
	}

	/// <summary>
	/// Gets or sets the <see cref="System.Byte"/> with the specified index.
	/// </summary>
	public byte this[int index] {
		get {
			CheckDisposed();
			if (index < 0 || index > _length)
				throw new ArgumentOutOfRangeException("index");
			Position l = GetPositionFor(index);
			ArraySegment<byte> buffer = _buffers[l.Index];
			return buffer.Array[buffer.Offset + l.Offset];
		}
		set {
			CheckDisposed();
			if (index < 0)
				throw new ArgumentException("_Index");
			Position l = GetPositionFor(index);
			EnsureCapacity(l);
			ArraySegment<byte> buffer = _buffers[l.Index];
			buffer.Array[buffer.Offset + l.Offset] = value;
			if (_length <= index)
				_length = index + 1;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BufferPool"/> class.
	/// </summary>
	public BufferPool() : this(1, BufferManager.Default) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BufferPool"/> class.
	/// </summary>
	/// <param name="bufferManager">The buffer manager.</param>
	public BufferPool(BufferManager bufferManager) : this(1, bufferManager) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BufferPool"/> class.
	/// </summary>
	/// <param name="initialBufferCount">The number of initial buffers.</param>
	/// <param name="bufferManager">The buffer manager.</param>
	public BufferPool(int initialBufferCount, BufferManager bufferManager) {
		if (initialBufferCount <= 0)
			throw new ArgumentException("initialBufferCount");
		if (bufferManager == null)
			throw new ArgumentNullException("bufferManager");
		_length = 0;
		_buffers = new List<ArraySegment<byte>>(bufferManager.CheckOut(initialBufferCount));
		// must have 1 buffer
		_chunkSize = _buffers[0].Count;
		_bufferManager = bufferManager;
		_disposed = false;
	}

	/// <summary>
	/// Appends the specified data.
	/// </summary>
	/// <param name="data">The data to write.</param>
	public void Append(byte[] data) {
		if (data == null)
			throw new ArgumentNullException("data");
		Write(_length, data, 0, data.Length);
	}

	/// <summary>
	/// Appends the specified data.
	/// </summary>
	/// <param name="data">The data.</param>
	/// <param name="offset">The offset.</param>
	/// <param name="count">The count.</param>
	public void Append(byte[] data, int offset, int count) {
		Write(_length, data, offset, count);
	}

	/// <summary>
	/// Writes data to the specified position.
	/// </summary>
	/// <param name="position">The position to write at.</param>
	/// <param name="data">The data.</param>
	/// <param name="offset">The offset.</param>
	/// <param name="count">The count.</param>
	public void Write(int position, byte[] data, int offset, int count) {
		if (data == null)
			throw new ArgumentNullException("data");
		if (offset < 0 || offset > data.Length)
			throw new ArgumentOutOfRangeException("offset");
		if (count < 0 || count + offset > data.Length)
			throw new ArgumentOutOfRangeException("count");
		Write(position, new ArraySegment<byte>(data, offset, count));
	}

	/// <summary>
	/// Writes data to the specified position.
	/// </summary>
	/// <param name="position">The position to write at.</param>
	/// <param name="data">The data.</param>
	public void Write(int position, ArraySegment<byte> data) {
		CheckDisposed();
		int written = 0;
		int tmpLength = position;
		do {
			Position loc = GetPositionFor(tmpLength);
			EnsureCapacity(loc);
			ArraySegment<byte> current = _buffers[loc.Index];
			int canWrite = data.Count - written;
			int available = current.Count - loc.Offset;
			canWrite = canWrite > available ? available : canWrite;
			if (canWrite > 0)
				Buffer.BlockCopy(data.Array, written + data.Offset, current.Array, current.Offset + loc.Offset,
					canWrite);
			written += canWrite;
			tmpLength += canWrite;
		} while (written < data.Count);

		_length = tmpLength > _length ? tmpLength : _length;
	}

	/// <summary>
	/// Reads data from a given position
	/// </summary>
	/// <param name="position">The position to read from.</param>
	/// <param name="data">Where to read the data to.</param>
	/// <param name="offset">The offset to start reading into.</param>
	/// <param name="count">The number of bytes to read.</param>
	/// <returns></returns>
	public int ReadFrom(int position, byte[] data, int offset, int count) {
		if (data == null)
			throw new ArgumentNullException("data");
		if (offset < 0 || offset > data.Length)
			throw new ArgumentOutOfRangeException("offset");
		if (count < 0 || count + offset > data.Length)
			throw new ArgumentOutOfRangeException("count");
		return ReadFrom(position, new ArraySegment<byte>(data, offset, count));
	}

	/// <summary>
	/// Reads data from a given position.
	/// </summary>
	/// <param name="position">The position to read from.</param>
	/// <param name="data">Where to read the data to.</param>
	/// <returns></returns>
	public int ReadFrom(int position, ArraySegment<byte> data) {
		CheckDisposed();
		if (position >= _length)
			return 0;
		int copied = 0;
		int left = Math.Min(data.Count, _length - position);
		int currentLocation = position;
		while (left > 0) {
			Position l = GetPositionFor(currentLocation);
			ArraySegment<byte> current = _buffers[l.Index];
			int bytesToRead = Math.Min(_chunkSize - l.Offset, left);
			if (bytesToRead > 0) {
				Buffer.BlockCopy(current.Array, current.Offset + l.Offset, data.Array, data.Offset + copied,
					bytesToRead);
				copied += bytesToRead;
				left -= bytesToRead;
				currentLocation += bytesToRead;
			}
		}

		return copied;
	}

	/// <summary>
	/// Sets the length of the <see cref="BufferPool"></see>
	/// </summary>
	/// <param name="newLength">The new length.</param>
	public void SetLength(int newLength) {
		SetLength(newLength, true);
	}

	/// <summary>
	/// Sets the length of the <see cref="BufferPool"></see>
	/// </summary>
	/// <param name="newLength">The new length</param>
	/// <param name="releaseMemory">if set to <c>true</c> any memory no longer used will be released.</param>
	public void SetLength(int newLength, bool releaseMemory) {
		CheckDisposed();
		if (newLength < 0) throw new ArgumentException("newLength must be greater than 0");
		int oldCapacity = Capacity;
		_length = newLength;

		if (_length < oldCapacity && releaseMemory)
			RemoveCapacity(GetPositionFor(_length));
		else if (_length > oldCapacity)
			EnsureCapacity(GetPositionFor(_length));
	}

	private void RemoveCapacity(Position position) {
		while (_buffers.Count > position.Index + 1) {
			_bufferManager.CheckIn(_buffers[_buffers.Count - 1]);
			_buffers.RemoveAt(_buffers.Count - 1);
		}
	}

	private void EnsureCapacity(Position position) {
		if (position.Index >= _buffers.Count) {
			foreach (ArraySegment<byte> buffer in _bufferManager.CheckOut(position.Index + 1 - _buffers.Count)) {
				if (buffer.Count != _chunkSize)
					throw new Exception("Received a buffer of the wrong size: this should never happen.");
				_buffers.Add(buffer);
			}
		}
	}

	/// <summary>
	/// Converts this <see cref="BufferPool"></see> to a byte array.
	/// </summary>
	/// <returns></returns>
	public byte[] ToByteArray() {
		CheckDisposed();
		Position l = GetPositionFor(_length);
		var result = new byte[_length];
		for (int i = 0; i < l.Index; i++) {
			ArraySegment<byte> current = _buffers[i];
			//copy full buffers
			Buffer.BlockCopy(current.Array, current.Offset, result, i * _chunkSize, _chunkSize);
		}

		//copy last partial buffer
		if (l.Index < _buffers.Count) {
			ArraySegment<byte> last = _buffers[l.Index];
			Buffer.BlockCopy(last.Array, last.Offset, result, l.Index * _chunkSize, l.Offset);
		}

		return result;
	}

	private Position GetPositionFor(int index) {
		//we could do this much faster if we restricted buffer sizes to powers of 2
		var l = new Position(index / _chunkSize, index % _chunkSize);
		return l;
	}

	/// <summary>
	/// Returns any memory used buy this <see cref="BufferPool"></see> to the <see cref="BufferManager"></see>
	/// </summary>
	public void Dispose() {
		DisposeInternal();
		GC.SuppressFinalize(this);
	}

	~BufferPool() {
		DisposeInternal();
	}

	protected virtual void DisposeInternal() {
		if (_buffers != null)
			_bufferManager.CheckIn(_buffers);
		_disposed = true;
		_buffers = null;
	}

	private void CheckDisposed() {
		if (_disposed)
			throw new ObjectDisposedException("Object has been disposed.");
	}
}
