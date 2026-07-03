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
	private async Task<FlightInfo> PrepareQueryAsync(CommandStatementQuery query, FlightDescriptor descriptor, CancellationToken token) {
		using var buffer = Encoding.UTF8.GetBytes(query.Query, allocator: null);
		var preparedQuery = await engine.PrepareQueryAsync(buffer.Memory, new() { UseDigitalSignature = true }, token);
		return GetQueryInfo(preparedQuery, descriptor, discoverSchema: false);
	}

	private async Task<FlightInfo> PrepareQueryAsync(ReadOnlyMemory<byte> query, FlightDescriptor descriptor, CancellationToken token) {
		var preparedQueryBuffer = await engine.PrepareQueryAsync(query, new() { UseDigitalSignature = true }, token);
		return GetQueryInfo(preparedQueryBuffer, descriptor, discoverSchema: true);
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
