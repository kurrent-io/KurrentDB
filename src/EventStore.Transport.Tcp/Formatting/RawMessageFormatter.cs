// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.BufferManagement;

namespace EventStore.Transport.Tcp.Formatting;

/// <summary>
/// Formatter which does not format anything, actually. Just outputs raw byte[].
/// </summary>
public class RawMessageFormatter : IMessageFormatter<byte[]> {
	private readonly BufferManager _bufferManager;
	private readonly int _initialBuffers;

	/// <summary>
	/// Initializes a new instance of the <see cref="RawMessageFormatter"/> class.
	/// </summary>
	public RawMessageFormatter() : this(BufferManager.Default, 2) {
	}


	/// <summary>
	/// Initializes a new instance of the <see cref="RawMessageFormatter"/> class.
	/// </summary>
	/// <param name="bufferManager">The buffer manager.</param>
	public RawMessageFormatter(BufferManager bufferManager) : this(bufferManager, 2) {
	}


	/// <summary>
	/// Initializes a new instance of the <see cref="RawMessageFormatter"/> class.
	/// </summary>
	/// <param name="bufferManager">The buffer manager.</param>
	/// <param name="initialBuffers">The number of initial buffers.</param>
	public RawMessageFormatter(BufferManager bufferManager, int initialBuffers) {
		_bufferManager = bufferManager;
		_initialBuffers = initialBuffers;
	}

	public BufferPool ToBufferPool(byte[] message) {
		if (message == null) throw new ArgumentNullException("message");

		var bufferPool = new BufferPool(_initialBuffers, _bufferManager);
		var stream = new BufferPoolStream(bufferPool);
		stream.Write(message, 0, message.Length);
		return bufferPool;
	}

	public ArraySegment<byte> ToArraySegment(byte[] message) {
		if (message == null) throw new ArgumentNullException("message");
		return new ArraySegment<byte>(message, 0, message.Length);
	}

	public byte[] ToArray(byte[] message) {
		if (message == null) throw new ArgumentNullException("message");
		return message;
	}

	public byte[] From(BufferPool bufferPool) {
		return bufferPool.ToByteArray();
	}

	public byte[] From(ArraySegment<byte> segment) {
		var msg = new byte[segment.Count];
		Buffer.BlockCopy(segment.Array, segment.Offset, msg, 0, segment.Count);
		return msg;
	}

	public byte[] From(byte[] array) {
		return array;
	}
}
