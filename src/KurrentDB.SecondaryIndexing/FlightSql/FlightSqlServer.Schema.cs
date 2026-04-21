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
	private Task<Schema> GetQuerySchemaAsync(ReadOnlyMemory<char> query, CancellationToken token) {
		Task<Schema> task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled<Schema>(token);
		} else {
			var buffer = default(MemoryOwner<byte>);
			try {
				buffer = Encoding.UTF8.GetBytes(query.Span, allocator: null);
				var tmp = engine.PrepareQuery(buffer.Span, new() { UseDigitalSignature = false });
				buffer.Dispose();
				buffer = tmp;

				task = Task.FromResult(engine.GetArrowSchema(buffer.Span));
			} catch (Exception e) {
				task = Task.FromException<Schema>(e);
			} finally {
				buffer.Dispose();
			}
		}

		return task;
	}

	private static ByteString SerializeSchema(Schema schema) {
		var buffer = new PoolingBufferWriter<byte> { Capacity = 2048 };
		var stream = Stream.CreateWritable(buffer, flush: null, flushAsync: null);
		var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true);
		try {
			writer.WriteStart();
			return ByteString.CopyFrom(buffer.WrittenMemory.Span);
		} finally {
			writer.Dispose();
			stream.Dispose();
			buffer.Dispose();
		}
	}
}
