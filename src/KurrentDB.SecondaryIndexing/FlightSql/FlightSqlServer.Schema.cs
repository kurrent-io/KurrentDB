// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using DotNext.Buffers;
using DotNext.IO;
using DotNext.Text;
using Google.Protobuf;

namespace KurrentDB.SecondaryIndexing.FlightSql;

partial class FlightSqlServer {
	private async Task<Schema> GetQuerySchemaAsync(ReadOnlyMemory<char> query, CancellationToken token) {
		var buffer = default(MemoryOwner<byte>);
		try {
			buffer = Encoding.UTF8.GetBytes(query.Span, allocator: null);
			var prepared = await engine.PrepareQueryAsync(buffer.Memory, new() { UseDigitalSignature = false }, token);
			buffer.Dispose();
			buffer = prepared;

			return engine.GetArrowSchema(buffer.Span);
		} finally {
			buffer.Dispose();
		}
	}

	private ByteString SerializeSchema(Schema schema) {
		var stream = Stream.CreateWritable(bufferWriter, flush: null, flushAsync: null);
		var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true);
		try {
			writer.WriteStart();
			return WrapAndRegisterOnDispose(bufferWriter.DetachBuffer());
		} finally {
			writer.Dispose();
			stream.Dispose();
		}
	}
}
