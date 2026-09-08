// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Buffers;
using DotNext.IO;
using Google.Protobuf;

namespace KurrentDB.KontrolPlane.Raft.StateMachine.LogEntries;

internal interface IProtobufSerializable<TSelf> : IMessage<TSelf>
	where TSelf : class, IProtobufSerializable<TSelf> {

	public static abstract MessageParser<TSelf> Parser { get; }
}

internal static class ProtobufSerializer {
	public static ValueTask WriteToAsync<T, TWriter>(this T obj, TWriter writer, CancellationToken token)
		where T : class, IProtobufSerializable<T>
		where TWriter : IAsyncBinaryWriter{
		ValueTask task;
		if (writer.TryGetBufferWriter() is { } buffer) {
			// fast path
			task = ValueTask.CompletedTask;
			try {
				obj.WriteTo(buffer);
			} catch (Exception e) {
				task = ValueTask.FromException(e);
			}
		} else {
			// slow path
			task = SerializeSlowAsync(obj, writer, token);
		}

		return task;

		static async ValueTask SerializeSlowAsync(T state, IAsyncBinaryWriter writer, CancellationToken token) {
			using var buffer = MemoryAllocator<byte>.Default.AllocateExactly(state.CalculateSize());
			state.WriteTo(buffer.Span);
			await writer.WriteAsync(buffer.Memory, token: token).ConfigureAwait(false);
		}
	}
}
