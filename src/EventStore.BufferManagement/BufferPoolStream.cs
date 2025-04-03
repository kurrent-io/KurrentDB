// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;

namespace EventStore.BufferManagement;

public class BufferPoolStream : Stream {
	private readonly BufferPool _bufferPool;
	private long _position;

	public BufferPool BufferPool {
		get { return _bufferPool; }
	}

	public override bool CanRead {
		get { return true; }
	}

	public override bool CanSeek {
		get { return true; }
	}

	public override bool CanWrite {
		get { return true; }
	}

	public override long Length {
		get { return _bufferPool.Length; }
	}

	public int Capacity {
		get { return _bufferPool.Capacity; }
	}

	public override long Position {
		get { return _position; }
		set {
			if (value < 0 || value > _bufferPool.Length)
				throw new ArgumentOutOfRangeException("value");
			_position = value;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BufferPoolStream"/> class.
	/// </summary>
	/// <param name="bufferPool">The buffer pool used as underlying storage.</param>
	public BufferPoolStream(BufferPool bufferPool) {
		if (bufferPool == null)
			throw new ArgumentNullException("bufferPool");
		_bufferPool = bufferPool;
	}

	public override void Flush() {
		//noop
	}

	public override long Seek(long offset, SeekOrigin origin) {
		switch (origin) {
			case SeekOrigin.Begin:
				Position = offset;
				break;
			case SeekOrigin.End:
				Position = _bufferPool.Length + offset;
				break;
			case SeekOrigin.Current:
				Position = _position + offset;
				break;
			default:
				throw new Exception("Unknown SeekOrigin: " + origin.ToString());
		}

		return Position;
	}

	public override void SetLength(long value) {
		_bufferPool.SetLength((int)value);
		if (_position > value)
			_position = value;
	}

	public override int Read(byte[] buffer, int offset, int count) {
		if (_position >= _bufferPool.Length)
			return 0;
		int ret = _bufferPool.ReadFrom((int)_position, buffer, offset, count);
		_position += ret;
		return ret;
	}

	public override void Write(byte[] buffer, int offset, int count) {
		_bufferPool.Write((int)_position, buffer, offset, count);
		_position += count;
	}

	protected override void Dispose(bool disposing) {
		if (disposing)
			_bufferPool.Dispose();
		base.Dispose(disposing);
	}

	public byte[] ToArray() {
		return _bufferPool.ToByteArray();
	}
}
