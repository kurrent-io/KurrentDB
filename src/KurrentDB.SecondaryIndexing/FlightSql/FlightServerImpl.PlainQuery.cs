// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using System.Text;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Arrow.Flight.Protocol.Sql;
using DotNext.Buffers;
using DotNext.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Quack.Arrow;
using KurrentDB.SecondaryIndexing.Query;

namespace KurrentDB.SecondaryIndexing.FlightSql;

partial class FlightServerImpl {
	private Task<FlightInfo> PrepareQueryAsync(CommandStatementQuery query, FlightDescriptor descriptor, CancellationToken token) {
		Task<FlightInfo> task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled<FlightInfo>(token);
		} else {
			try {
				task = Task.FromResult(PrepareQuery(query, descriptor));
			} catch (Exception e) {
				task = Task.FromException<FlightInfo>(e);
			}
		}

		return task;
	}

	private Task<FlightInfo> PrepareQueryAsync(ReadOnlySpan<byte> query, FlightDescriptor descriptor, CancellationToken token) {
		Task<FlightInfo> task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled<FlightInfo>(token);
		} else {
			var preparedQueryBuffer = default(MemoryOwner<byte>);
			try {
				preparedQueryBuffer = engine.PrepareQuery(query, new() { UseDigitalSignature = true });
				task = Task.FromResult(GetQueryInfo(preparedQueryBuffer.Span, descriptor, discoverSchema: true));
			} catch (Exception e) {
				task = Task.FromException<FlightInfo>(e);
			} finally {
				preparedQueryBuffer.Dispose();
			}
		}

		return task;
	}

	private FlightInfo PrepareQuery(CommandStatementQuery query, FlightDescriptor descriptor) {
		var buffer = default(MemoryOwner<byte>);
		try {
			buffer = Encoding.UTF8.GetBytes(query.Query, allocator: null);
			var tmp = engine.PrepareQuery(buffer.Span, new() { UseDigitalSignature = true });
			buffer.Dispose();
			buffer = tmp;

			return GetQueryInfo(buffer.Span, descriptor, discoverSchema: false);
		} finally {
			buffer.Dispose();
		}
	}

	private FlightInfo GetQueryInfo(ReadOnlySpan<byte> preparedQuery, FlightDescriptor descriptor, bool discoverSchema)
		=> GetQueryInfo(ByteString.CopyFrom(preparedQuery), descriptor, discoverSchema);

	private FlightInfo GetQueryInfo(ByteString preparedQuery, FlightDescriptor descriptor, bool discoverSchema) {
		var encodedQuery = new BytesValue { Value = preparedQuery };
		var ep = new FlightEndpoint(new FlightTicket(Any.Pack(encodedQuery).ToByteString()), []);
		return new(
			discoverSchema ? engine.GetArrowSchema(preparedQuery.Span) : EmptySchema,
			descriptor,
			[ep]);
	}

	private Task ExecuteQueryAsync(ByteString query, FlightServerRecordBatchStreamWriter writer, CancellationToken token)
		=> engine.ExecuteAsync(query.Memory, new QueryResultConsumer(writer), new() { CheckIntegrity = true }, token)
			.AsTask();

	[StructLayout(LayoutKind.Auto)]
	private readonly struct QueryResultConsumer(FlightServerRecordBatchStreamWriter writer) : IQueryResultConsumer {

		public ValueTask ConsumeAsync(IQueryResultReader reader, CancellationToken token)
			=> ConsumeAsync(reader, writer, token);

		private static async ValueTask ConsumeAsync(IQueryResultReader reader,
			FlightServerRecordBatchStreamWriter writer,
			CancellationToken token) {
			using var options = reader.GetArrowOptions();
			var schema = reader.GetArrowSchema(options);
			await writer.SetupStream(schema);

			while (reader.TryRead()) {
				using var scope = reader.Chunk.ToRecordBatch(options, schema, out var batch);
				await writer.WriteAsync(batch);
				token.ThrowIfCancellationRequested();
			}
		}
	}
}
