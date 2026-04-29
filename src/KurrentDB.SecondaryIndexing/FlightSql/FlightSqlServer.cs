// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using Apache.Arrow;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Apache.Arrow.Flight.Sql;
using Arrow.Flight.Protocol.Sql;
using EventStore.Plugins.Authorization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Query;
using KurrentDB.Core.Services.Transport.Grpc;
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
/// <param name="authProvider">Authorization provider.</param>
/// <param name="license">Tracks whether the Arrow Flight SQL entitlement is currently licensed.</param>
internal sealed partial class FlightSqlServer(IQueryEngine engine, IAuthorizationProvider authProvider, FlightSqlLicense license) : FlightServer {
	private static readonly Operation ReadOperation = new Operation(Operations.Streams.Read)
		.WithParameter(Operations.Streams.Parameters.StreamId(SystemStreams.AllStream));

	public override async Task Handshake(
		IAsyncStreamReader<FlightHandshakeRequest> requestStream,
		IAsyncStreamWriter<FlightHandshakeResponse> responseStream,
		ServerCallContext context) {

		EnsureLicensed();

		// for JDBC tooling: return the basic credentials as the bearer token so that JDBC will
		// submit it as the bearer token in subsequent requests.
		if (context.RequestHeaders.GetValue("authorization") is
			['B' or 'b', 'a', 's', 'i', 'c', ' ', .. var credentials]) {
			await context.WriteResponseHeadersAsync(new Metadata {
				{ "authorization", $"Bearer {credentials}" }
			});
		}

		await responseStream.WriteAsync(new FlightHandshakeResponse());
	}

	public override async Task<FlightInfo> GetFlightInfo(FlightDescriptor request, ServerCallContext context) {
		EnsureLicensed();
		if (!await authProvider.CheckAccessAsync(context.User, ReadOperation, context.CancellationToken))
			throw RpcExceptions.AccessDenied();

		// A google.protobuf.Any message has a specific binary signature. It consists of two fields:
		// Field 1 (type_url): Tag 0x0A (field number 1, wire type 2).
		// Field 2 (value): Tag 0x12 (field number 2, wire type 2).
		// We can use 0x0A as a discriminator to distinguish between Arrow Flight and Arrow Flight SQL
		const byte discriminator = 0x0A;

		// process Arrow Flight command
		if (request.Command.Span is [not discriminator, ..])
			return await PrepareQueryAsync(request.Command.Span, request, context.CancellationToken);

		var state = context.GetHttpContext().Features.Get<ConnectionState>() ?? throw WrongServerState();

		// process Arrow Flight SQL command
		return FlightSqlServerHelpers.GetCommand(request) switch {
			CommandStatementQuery query =>
				// TODO: Ad-hoc query execution doesn't return the schema for the dataset. It's not possible to check
				// the potential problem with it since all major FlightSql clients use prepared statement execution
				await PrepareQueryAsync(query, request, context.CancellationToken),
			CommandPreparedStatementQuery query => await GetPreparedStatementSchemaAsync(query.PreparedStatementHandle, state, request,
				context.CancellationToken),
			CommandGetSqlInfo or
			CommandGetCatalogs or
			CommandGetDbSchemas or
			CommandGetTables or
			CommandGetTableTypes or
			CommandGetPrimaryKeys or
			CommandGetExportedKeys or
			CommandGetImportedKeys or
			CommandGetCrossReference => EmptyFlightInfo(request),
			_ => throw FeatureNotSupported()
		};
	}

	private static FlightInfo EmptyFlightInfo(FlightDescriptor descriptor) =>
		new(new Schema([], []), descriptor, []);

	public override async Task DoGet(FlightTicket ticket, FlightServerRecordBatchStreamWriter responseStream, ServerCallContext context) {
		EnsureLicensed();
		if (!await authProvider.CheckAccessAsync(context.User, ReadOperation, context.CancellationToken))
			throw RpcExceptions.AccessDenied();

		var parsedTicket = Any.Parser.ParseFrom(ticket.Ticket);

		if (parsedTicket.Is(BytesValue.Descriptor)) {
			// execute plain query
			await ExecuteQueryAsync(parsedTicket.Unpack<BytesValue>().Value, responseStream, context.CancellationToken);
		} else if (context.GetHttpContext().Features.Get<ConnectionState>() is not { } state) {
			throw WrongServerState();
		} else if (parsedTicket.Is(CommandPreparedStatementQuery.Descriptor)) {
			// execute prepared statement
			await ExecutePreparedStatementAsync(
				parsedTicket.Unpack<CommandPreparedStatementQuery>().PreparedStatementHandle.Memory,
				state,
				responseStream, context.CancellationToken);
		} else {
			throw FeatureNotSupported();
		}
	}

	public override async Task DoAction(FlightAction action, IAsyncStreamWriter<FlightResult> response, ServerCallContext context) {
		EnsureLicensed();
		if (!await authProvider.CheckAccessAsync(context.User, ReadOperation, context.CancellationToken))
			throw RpcExceptions.AccessDenied();

		var state = context.GetHttpContext().Features.Get<ConnectionState>() ?? throw WrongServerState();

		switch (action.Type) {
			case SqlAction.CreateRequest:
				// Create prepared statement
				await CreatePreparedStatementAsync(
					FlightSqlUtils.ParseAndUnpack<ActionCreatePreparedStatementRequest>(action.Body),
					state,
					response,
					context.CancellationToken);
				break;
			case SqlAction.CloseRequest:
				// Destroy prepared statement
				await ClosePreparedStatementAsync(
					FlightSqlUtils.ParseAndUnpack<ActionClosePreparedStatementRequest>(action.Body).PreparedStatementHandle.Memory,
					state,
					response,
					context.CancellationToken);
				break;
			case "CloseSession":
				// Python driver sends this undocumented action type, which is no-op in our case
				await response.WriteAsync(new FlightResult(ByteString.Empty));
				break;
			default:
				throw CreateException(StatusCode.Unimplemented,
					$"Action {action.Type} is not supported");
		}
	}

	public override async Task DoPut(FlightServerRecordBatchStreamReader requestStream,
		IAsyncStreamWriter<FlightPutResult> responseStream,
		ServerCallContext context) {
		EnsureLicensed();
		if (!await authProvider.CheckAccessAsync(context.User, ReadOperation, context.CancellationToken))
			throw RpcExceptions.AccessDenied();

		await DoPut(FlightSqlServerHelpers.GetCommand(await requestStream.FlightDescriptor), requestStream,
			responseStream, context);
	}

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

	public override async Task<Schema> GetSchema(FlightDescriptor request, ServerCallContext context) {
		EnsureLicensed();
		if (!await authProvider.CheckAccessAsync(context.User, ReadOperation, context.CancellationToken))
			throw RpcExceptions.AccessDenied();

		var state = context.GetHttpContext().Features.Get<ConnectionState>() ?? throw WrongServerState();

		return FlightSqlServerHelpers.GetCommand(request) switch {
			CommandStatementQuery query => await GetQuerySchemaAsync(query.Query.AsMemory(), context.CancellationToken),
			CommandPreparedStatementQuery query => await GetPreparedStatementSchemaAsync(
				query.PreparedStatementHandle.Memory,
				state,
				context.CancellationToken),
			_ => throw FeatureNotSupported(),
		};
	}

	private static RpcException CreateException(StatusCode code, string message)
		=> new(new Status(code, message));

	private static RpcException WrongServerState()
		=> CreateException(StatusCode.Internal, "The operation is not available due to server state");

	private static RpcException FeatureNotSupported()
		=> CreateException(StatusCode.Unimplemented, "The feature is not supported");

	private void EnsureLicensed() {
		if (!license.IsLicensed)
			throw CreateException(StatusCode.FailedPrecondition,
				$"Arrow Flight SQL is not licensed. Required entitlement: {FlightSqlLicense.Entitlement}");
	}
}

file static class ServerCallContextExtensions {
	extension(ServerCallContext context) {
		public ClaimsPrincipal User => context.GetHttpContext().User;
	}
}
