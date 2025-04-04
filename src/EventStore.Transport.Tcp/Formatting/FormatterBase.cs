// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.BufferManagement;

namespace EventStore.Transport.Tcp.Formatting;

public abstract class FormatterBase<T> : IMessageFormatter<T> {
	/// <summary>
	/// Gets a <see cref="BufferPool"></see> representing the IMessage provided.
	/// </summary>
	/// <param name="message">The message.</param>
	/// <returns>A <see cref="BufferPool"></see> with a representation of the message</returns>
	public abstract BufferPool ToBufferPool(T message);

	/// <summary>
	/// converts the message to a <see cref="ArraySegment{T}"></see>
	/// </summary>
	/// <param name="message">The message.</param>
	/// <returns></returns>
	public virtual ArraySegment<byte> ToArraySegment(T message) {
		return new ArraySegment<byte>(ToArray(message));
	}

	/// <summary>
	/// Converts the message to a byte array
	/// </summary>
	/// <param name="message">The message.</param>
	/// <returns></returns>
	public virtual byte[] ToArray(T message) {
		using (BufferPool pool = ToBufferPool(message)) {
			return pool.ToByteArray();
		}
	}

	/// <summary>
	/// Gets a message from a <see cref="BufferPool"/>
	/// </summary>
	/// <param name="bufferPool">The BufferPool to get data from.</param>
	/// <returns></returns>
	public virtual T From(BufferPool bufferPool) {
		if (bufferPool == null)
			throw new ArgumentNullException("bufferPool");
		var stream = new BufferPoolStream(bufferPool);
		return From(stream);
	}

	/// <summary>
	/// Gets a message from a <see cref="ArraySegment{Byte}"></see>
	/// </summary>
	/// <param name="segment">The segment containing the raw data.</param>
	/// <returns></returns>
	public virtual T From(ArraySegment<byte> segment) {
		using (var stream = new MemoryStream(segment.Array, segment.Offset, segment.Count, false)) {
			return From(stream);
		}
	}

	/// <summary>
	/// Gets a message from a byte array
	/// </summary>
	/// <param name="array">The byte array.</param>
	/// <returns></returns>
	public virtual T From(byte[] array) {
		if (array == null)
			throw new ArgumentNullException("array");
		using (var stream = new MemoryStream(array, 0, array.Length, false)) {
			return From(stream);
		}
	}

	/// <summary>
	/// Creates a message object from the specified stream
	/// </summary>
	/// <param name="stream">The stream.</param>
	/// <returns></returns>
	public abstract T From(Stream stream);
}
