// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Apache.Arrow.Flight.Sql;
using Arrow.Flight.Protocol.Sql;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.SecondaryIndexing.Query;

namespace KurrentDB.SecondaryIndexing.FlightSql;

internal sealed partial class FlightServerImpl(IQueryEngine engine) : FlightServer {
	private static readonly Schema EmptySchema = new([], []);

	public override Task<FlightInfo> GetFlightInfo(FlightDescriptor request, ServerCallContext context) {
		//A google.protobuf.Any message has a specific binary signature. It consists of two fields:
		// Field 1 (type_url): Tag 0x0A (field number 1, wire type 2).
		// Field 2 (value): Tag 0x12 (field number 2, wire type 2).
		// We can use 0x0A as a discriminator to distinguish between Arrow Flight and Arrow Flight SQL
		const byte discriminator = 0x0A;

		// process Arrow Flight command
		if (request.Command.Span is [not discriminator, ..])
			return PrepareQueryAsync(request.Command.Span, request, context.CancellationToken);

		// process Arrow Flight SQL command
		return FlightSqlServer.GetCommand(request) switch {
			CommandStatementQuery query => PrepareQueryAsync(query, request, context.CancellationToken),
			_ => Task.FromException<FlightInfo>(new RpcException(new Status(StatusCode.Unimplemented, "The feature is not supported")))
		};
	}

	public override Task DoGet(FlightTicket ticket, FlightServerRecordBatchStreamWriter responseStream, ServerCallContext context) {
		var parsedTicket = Any.Parser.ParseFrom(ticket.Ticket);

		// execute plain query
		if (parsedTicket.Is(BytesValue.Descriptor))
			return ExecuteQueryAsync(parsedTicket.Unpack<BytesValue>().Value, responseStream, context.CancellationToken);

		return Task.FromException<FlightInfo>(new RpcException(new Status(StatusCode.Unimplemented, "The feature is not supported")));
	}

	public override Task<Schema> GetSchema(FlightDescriptor request, ServerCallContext context) {
		return FlightSqlServer.GetCommand(request) switch {
			CommandStatementQuery query => GetSchemaAsync(query.Query.AsMemory(), context.CancellationToken),
			_ => Task.FromException<Schema>(new RpcException(new Status(StatusCode.Unimplemented, "The feature is not supported")))
		};
	}
}
