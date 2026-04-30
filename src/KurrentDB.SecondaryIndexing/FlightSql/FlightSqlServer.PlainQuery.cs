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

partial class FlightSqlServer {
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
			try {
				var preparedQueryBuffer = engine.PrepareQuery(query, new() { UseDigitalSignature = true });
				task = Task.FromResult(GetQueryInfo(preparedQueryBuffer, descriptor, discoverSchema: true));
			} catch (Exception e) {
				task = Task.FromException<FlightInfo>(e);
			}
		}

		return task;
	}

	private FlightInfo PrepareQuery(CommandStatementQuery query, FlightDescriptor descriptor) {
		MemoryOwner<byte> preparedQuery;
		using (var buffer = Encoding.UTF8.GetBytes(query.Query, allocator: null)) {
			preparedQuery = engine.PrepareQuery(buffer.Span, new() { UseDigitalSignature = true });
		}

		return GetQueryInfo(preparedQuery, descriptor, discoverSchema: false);
	}

	private FlightInfo GetQueryInfo(in MemoryOwner<byte> preparedQuery, FlightDescriptor descriptor, bool discoverSchema)
		=> GetQueryInfo(WrapAndRegisterOnDispose(preparedQuery), descriptor, discoverSchema);

	private FlightInfo GetQueryInfo(ByteString preparedQuery, FlightDescriptor descriptor, bool discoverSchema) {
		var encodedQuery = new BytesValue { Value = preparedQuery };
		var ep = new FlightEndpoint(new FlightTicket(PackToAny(encodedQuery)), []);
		return new(
			discoverSchema ? engine.GetArrowSchema(preparedQuery.Span) : new([], []),
			descriptor,
			[ep]);
	}

	private Task ExecuteQueryAsync(ByteString query, FlightServerRecordBatchStreamWriter writer, CancellationToken token)
		=> engine.ExecuteAsync(query.Memory, new QueryResultConsumer(writer), new() { CheckIntegrity = true }, token)
			.AsTask();

	[StructLayout(LayoutKind.Auto)]
	private readonly struct QueryResultConsumer(FlightRecordBatchStreamWriter writer) : IQueryResultConsumer {

		public ValueTask ConsumeAsync(IQueryResultReader reader, CancellationToken token)
			=> ConsumeAsync(reader, writer, token);

		private static async ValueTask ConsumeAsync(
			IQueryResultReader reader,
			FlightRecordBatchStreamWriter writer,
			CancellationToken token) {
			using var options = reader.GetArrowOptions();
			var schema = reader.GetArrowSchema(options);
			await writer.SetupStream(schema);

			while (reader.TryRead()) {
				using (var batch = reader.Chunk.ToRecordBatch(options, schema)) {
					await writer.WriteAsync(batch);
				}

				token.ThrowIfCancellationRequested();
			}
		}
	}
}
