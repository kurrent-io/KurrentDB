// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext;
using DotNext.Buffers;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

namespace KurrentDB.SecondaryIndexing.FlightSql;

// This class has SCOPED lifetime, so we can release all rented buffers after the request
partial class FlightSqlServer : IDisposable {
	private const int InlineBuffersCount = 4;
	private const int InitialBufferCapacity = 2048;

	private readonly PoolingBufferWriter<byte> bufferWriter = new() { Capacity = InitialBufferCapacity };
	private InlineArray4<MemoryOwner<byte>> inlineBuffers;
	private List<MemoryOwner<byte>>? extraBuffers;
	private int bufferCount;

	private PoolingBufferWriter<byte> PrepareWriter() {
		bufferWriter.GetSpan(InitialBufferCapacity); // ensure capacity
		return bufferWriter;
	}

	private void RegisterOnDispose(in MemoryOwner<byte> buffer) {
		var index = bufferCount++;

		if (index < InlineBuffersCount) {
			inlineBuffers[index] = buffer;
		} else {
			extraBuffers ??= new();
			extraBuffers.Add(buffer);
		}
	}

	private ByteString WrapAndRegisterOnDispose(in MemoryOwner<byte> buffer) {
		var result = UnsafeByteOperations.UnsafeWrap(buffer.Memory);
		RegisterOnDispose(buffer);
		return result;
	}

	private ByteString PackToAny(IMessage message) {
		message.WriteTo(PrepareWriter());
		var buffer = bufferWriter.DetachBuffer();
		try {
			var value = new Any {
				TypeUrl = GetTypeUrl(message.Descriptor),
				Value = UnsafeByteOperations.UnsafeWrap(buffer.Memory)
			};

			value.WriteTo(PrepareWriter());
			return WrapAndRegisterOnDispose(bufferWriter.DetachBuffer());
		} finally {
			buffer.Dispose();
		}

		static string GetTypeUrl(MessageDescriptor descriptor)
			=> string.Concat("type.googleapis.com/", descriptor.FullName);
	}

	private ByteString PackRaw(IMessage message) {
		message.WriteTo(PrepareWriter());
		var buffer = bufferWriter.DetachBuffer();
		return UnsafeByteOperations.UnsafeWrap(buffer.Memory);
	}

	public void Dispose() {
		bufferWriter.Dispose();
		Disposable.Dispose<MemoryOwner<byte>>(inlineBuffers);
		Disposable.Dispose(CollectionsMarshal.AsSpan(extraBuffers));
		extraBuffers?.Clear();
	}
}
