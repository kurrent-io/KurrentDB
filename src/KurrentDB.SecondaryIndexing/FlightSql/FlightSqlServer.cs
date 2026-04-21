// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Apache.Arrow.Flight.Sql;
using Arrow.Flight.Protocol.Sql;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.SecondaryIndexing.Query;
using FlightSqlServerHelpers = Apache.Arrow.Flight.Sql.FlightSqlServer;

namespace KurrentDB.SecondaryIndexing.FlightSql;

/// <summary>
/// Represents FlightSql server implementation for KurrentDB.
/// </summary>
/// <remarks>
/// in DuckDB, the prepared statement is local to the connection. The statement can be prepared by using the specified connection,
/// and MUST BE executed on the same connection. We can't project this model to FlightSQL as-is, because in that case every
/// prepared statement handle must be associated with the connection. It means that every client connection must have its own
/// registry of the connections associated with the prepared statements. This is very expensive.
/// Instead, the prepared statement handle is just a unique identifier that represents the transformed SQL query.
/// The query is not prepared at DuckDB level, so on every execution the database needs to parse it, build the plan and execute.
/// In other words, we mimic the prepared statement concept in FlightSQL, which leads to lower performance than true
/// DuckDB prepared statement.
/// </remarks>
/// <param name="engine">The query engine.</param>
internal sealed partial class FlightSqlServer(IQueryEngine engine) : FlightServer {
	public override Task<FlightInfo> GetFlightInfo(FlightDescriptor request, ServerCallContext context) {
		//A google.protobuf.Any message has a specific binary signature. It consists of two fields:
		// Field 1 (type_url): Tag 0x0A (field number 1, wire type 2).
		// Field 2 (value): Tag 0x12 (field number 2, wire type 2).
		// We can use 0x0A as a discriminator to distinguish between Arrow Flight and Arrow Flight SQL
		const byte discriminator = 0x0A;

		// process Arrow Flight command
		if (request.Command.Span is [not discriminator, ..])
			return PrepareQueryAsync(request.Command.Span, request, context.CancellationToken);

		var state = context.GetHttpContext().Features.Get<ConnectionState>();
		if (state is null)
			return Task.FromException<FlightInfo>(WrongServerState());

		// process Arrow Flight SQL command
		return FlightSqlServerHelpers.GetCommand(request) switch {
			// TODO: Ad-hoc query execution doesn't return the schema for the dataset. It's not possible to check
			// the potential problem with it since all major FlightSql clients use prepared statement execution
			CommandStatementQuery query => PrepareQueryAsync(query, request, context.CancellationToken),
			CommandPreparedStatementQuery query => GetPreparedStatementSchemaAsync(
				query.PreparedStatementHandle,
				state,
				request,
				context.CancellationToken),
			_ => Task.FromException<FlightInfo>(FeatureNotSupported())
		};
	}

	public override Task DoGet(FlightTicket ticket, FlightServerRecordBatchStreamWriter responseStream, ServerCallContext context) {
		var parsedTicket = Any.Parser.ParseFrom(ticket.Ticket);

		// execute plain query
		if (parsedTicket.Is(BytesValue.Descriptor))
			return ExecuteQueryAsync(parsedTicket.Unpack<BytesValue>().Value, responseStream, context.CancellationToken);

		var state = context.GetHttpContext().Features.Get<ConnectionState>();
		if (state is null)
			return Task.FromException<FlightInfo>(WrongServerState());

		if (parsedTicket.Is(CommandPreparedStatementQuery.Descriptor))
			return ExecutePreparedStatementAsync(
				parsedTicket.Unpack<CommandPreparedStatementQuery>().PreparedStatementHandle.Memory,
				state,
				responseStream, context.CancellationToken);

		return Task.FromException<FlightInfo>(FeatureNotSupported());
	}

	public override Task DoAction(FlightAction action, IAsyncStreamWriter<FlightResult> response, ServerCallContext context) {
		var state = context.GetHttpContext().Features.Get<ConnectionState>();
		if (state is null)
			return Task.FromException(WrongServerState());

		return action.Type switch {
			// Create prepared statement
			SqlAction.CreateRequest => CreatePreparedStatementAsync(
				FlightSqlUtils.ParseAndUnpack<ActionCreatePreparedStatementRequest>(action.Body),
				state,
				response,
				context.CancellationToken),

			// Destroy prepared statement
			SqlAction.CloseRequest => ClosePreparedStatementAsync(
				FlightSqlUtils.ParseAndUnpack<ActionClosePreparedStatementRequest>(action.Body).PreparedStatementHandle.Memory,
				state,
				response,
				context.CancellationToken),

			// Python driver sends this undocumented action type, which is no-op in our case
			"CloseSession" => response.WriteAsync(new FlightResult(ByteString.Empty)),
			_ => Task.FromException(CreateException(StatusCode.Unimplemented,
				$"Action {action.Type} is not supported"))
		};
	}

	public override async Task DoPut(FlightServerRecordBatchStreamReader requestStream,
		IAsyncStreamWriter<FlightPutResult> responseStream,
		ServerCallContext context)
		=> await DoPut(FlightSqlServerHelpers.GetCommand(await requestStream.FlightDescriptor), requestStream,
			responseStream, context);

	private Task DoPut(IMessage? message,
		FlightServerRecordBatchStreamReader requestStream,
		IAsyncStreamWriter<FlightPutResult> responseStream,
		ServerCallContext context) {
		var state = context.GetHttpContext().Features.Get<ConnectionState>();
		if (state is null)
			return Task.FromException(WrongServerState());

		return message switch {
			CommandPreparedStatementQuery preparedStatement => BindPreparedStatementAsync(
				preparedStatement,
				state,
				requestStream,
				responseStream,
				context.CancellationToken),
			_ => Task.FromException(FeatureNotSupported()),
		};
	}

	public override Task<Schema> GetSchema(FlightDescriptor request, ServerCallContext context) {
		var state = context.GetHttpContext().Features.Get<ConnectionState>();
		if (state is null)
			return Task.FromException<Schema>(WrongServerState());

		return FlightSqlServerHelpers.GetCommand(request) switch {
			CommandStatementQuery query => GetQuerySchemaAsync(query.Query.AsMemory(), context.CancellationToken),
			CommandPreparedStatementQuery query => GetPreparedStatementSchemaAsync(
				query.PreparedStatementHandle.Memory,
				state,
				context.CancellationToken),
			_ => Task.FromException<Schema>(FeatureNotSupported())
		};
	}

	private static RpcException CreateException(StatusCode code, string message)
		=> new(new Status(code, message));

	private static RpcException WrongServerState()
		=> CreateException(StatusCode.Internal, "The operation is not available due to server state");

	private static RpcException FeatureNotSupported()
		=> CreateException(StatusCode.Unimplemented, "The feature is not supported");
}
