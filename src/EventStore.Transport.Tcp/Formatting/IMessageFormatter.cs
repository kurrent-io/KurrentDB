// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.BufferManagement;

namespace EventStore.Transport.Tcp.Formatting;

public interface IMessageFormatter<T> {
	/// <summary>
	/// Converts the object to a <see cref="BufferPool"></see> representing a binary format of it.
	/// </summary>
	/// <param name="message">The message to convert.</param>
	/// <returns>A <see cref="BufferPool"></see> containing the data representing the object.</returns>
	BufferPool ToBufferPool(T message);

	/// <summary>
	/// Converts the object to a <see cref="ArraySegment{Byte}"></see> representing a binary format of it.
	/// </summary>
	/// <param name="message">The message to convert.</param>
	/// <returns>A <see cref="BufferPool"></see> containing the data representing the object.</returns>
	ArraySegment<byte> ToArraySegment(T message);

	/// <summary>
	/// Converts the object to a byte array representing a binary format of it.
	/// </summary>
	/// <param name="message">The message to convert.</param>
	/// <returns>A <see cref="BufferPool"></see> containing the data representing the object.</returns>
	byte[] ToArray(T message);

	/// <summary>
	/// Takes a <see cref="BufferPool"></see> and converts its contents to a message object
	/// </summary>
	/// <param name="bufferPool">The buffer pool.</param>
	/// <returns>A message representing the data given</returns>
	T From(BufferPool bufferPool);

	/// <summary>
	/// Takes an ArraySegment and converts its contents to a message object
	/// </summary>
	/// <param name="segment">The buffer pool.</param>
	/// <returns>A message representing the data given</returns>
	T From(ArraySegment<byte> segment);

	/// <summary>
	/// Takes an Array and converts its contents to a message object
	/// </summary>
	/// <param name="array">The buffer pool.</param>
	/// <returns>A message representing the data given</returns>
	T From(byte[] array);
}
