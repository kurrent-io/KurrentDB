// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Apache.Arrow;
using DotNext.Buffers;
using DotNext.Text;

namespace KurrentDB.SecondaryIndexing.FlightSql;

partial class FlightServerImpl {
	private Task<Schema> GetSchemaAsync(ReadOnlyMemory<char> query, CancellationToken token) {
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
}
