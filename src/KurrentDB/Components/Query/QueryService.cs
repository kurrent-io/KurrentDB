// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using DotNext.Buffers;
using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Query;

namespace KurrentDB.Components.Query;

public static class QueryService {
	internal static async ValueTask<JsonDocument> ExecuteAdHocUserQuery(this IQueryEngine engine, string sql, CancellationToken token) {
		// Convert query result to JSON
		sql = $"SELECT to_json(sub_query) FROM ({sql}) sub_query LIMIT 100";


		var preparedQuery = default(MemoryOwner<byte>);
		var reader = new JsonReader();
		try {
			preparedQuery = engine.PrepareQuery(Encoding.UTF8.GetBytes(sql), new() { UseDigitalSignature = false });

			await engine.ExecuteAsync(preparedQuery.Memory, reader, new() { CheckIntegrity = false }, token);

			return reader.ToJson();
		} finally {
			preparedQuery.Dispose();
			reader.Dispose();
		}
	}

	private sealed class JsonReader : Disposable, IQueryResultConsumer {
		private readonly PoolingBufferWriter<byte> _writer = new() { Capacity = 4096 };

		public ValueTask ConsumeAsync(IQueryResultReader resultReader, CancellationToken token) {
			var task = ValueTask.CompletedTask;
			try {
				Consume(resultReader, token);
			} catch (OperationCanceledException e) when (e.CancellationToken == token) {
				task = ValueTask.FromCanceled(token);
			} catch (Exception e) {
				task = ValueTask.FromException(e);
			}

			return task;
		}

		private void Consume(IQueryResultReader resultReader, CancellationToken token) {
			_writer.Add((byte)'[');
			while (resultReader.TryRead()) {
				foreach (ref readonly var row in resultReader.Chunk[0].BlobRows) {
					_writer.Write(row.AsSpan());
					_writer.Add((byte)',');
					token.ThrowIfCancellationRequested();
				}
			}

			// Remove trailing comma
			_writer.WrittenCount--;
			_writer.Add((byte)']');
		}

		public JsonDocument ToJson() {
			// JsonDocument.Parse is not applicable here because the lifetime of the returned JsonDocument
			// is larger than the lifetime of the _writer which keeps the written memory.
			// JsonDocument.Parse keeps the reference to the original memory block that becomes released
			// when the reader is closed.
			var reader = new Utf8JsonReader(_writer.WrittenMemory.Span);
			return JsonDocument.ParseValue(ref reader);
		}

		protected override void Dispose(bool disposing) {
			if (disposing) {
				_writer.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
