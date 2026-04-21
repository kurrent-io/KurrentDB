// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Apache.Arrow;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Arrow.Flight.Protocol.Sql;
using DotNext.Runtime.InteropServices;
using Google.Protobuf;
using Grpc.Core;
using Kurrent.Quack.Arrow;
using KurrentDB.SecondaryIndexing.Query;

namespace KurrentDB.SecondaryIndexing.FlightSql;

partial class FlightSqlServer {
	private Task CreatePreparedStatementAsync(ActionCreatePreparedStatementRequest request,
		ConnectionState state,
		IAsyncStreamWriter<FlightResult> responseStream,
		CancellationToken token) {
		Task task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled<FlightResult>(token);
		} else {
			try {
				var result = PackToAny(CreatePreparedStatement(request, state));

				// WORKAROUND: CancellationToken is not supported by .NET implementation
				task = responseStream.WriteAsync(new FlightResult(result));
			} catch (Exception e) {
				task = Task.FromException(e);
			}
		}

		return task;
	}

	private ActionCreatePreparedStatementResult CreatePreparedStatement(ActionCreatePreparedStatementRequest request,
		ConnectionState state) {
		if (!state.CreatePreparedStatement(request.Query, out var handle, out var statement, out var parameters))
			throw CreateException(StatusCode.ResourceExhausted, "Too many prepared statements");

		return new ActionCreatePreparedStatementResult {
			DatasetSchema = SerializeSchema(statement.DatasetSchema),
			ParameterSchema = SerializeSchema(parameters),
			PreparedStatementHandle = ByteString.CopyFrom(MemoryMarshal.AsReadOnlyBytes(in handle)),
		};
	}

	private static Task ClosePreparedStatementAsync(
		ReadOnlyMemory<byte> handle,
		ConnectionState state,
		IAsyncStreamWriter<FlightResult> response,
		CancellationToken token) {
		Task task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled(token);
		} else {
			try {
				ClosePreparedStatement(handle.Span, state);

				// WORKAROUND: CancellationToken is not supported by .NET implementation
				task = response.WriteAsync(new(ByteString.Empty));
			} catch (Exception e) {
				task = Task.FromException(e);
			}
		}

		return task;
	}

	private static void ClosePreparedStatement(ReadOnlySpan<byte> handle, ConnectionState state) {
		if (!state.ClosePreparedStatement(new(handle))) {
			throw PreparedStatementNotFound();
		}
	}

	private async Task BindPreparedStatementAsync(CommandPreparedStatementQuery statement,
		ConnectionState state,
		FlightRecordBatchStreamReader request,
		IAsyncStreamWriter<FlightPutResult> response,
		CancellationToken token) {
		var handle = new Guid(statement.PreparedStatementHandle.Span);

		while (await request.MoveNext(token)) {
			if (!state.BindPreparedStatement(handle, request.Current))
				throw PreparedStatementNotFound();
		}

		// Python client ignores the result and its handle. This is bad, because we can't make
		// the server-side implementation stateless. We could encode the query and the binding parameters
		// inside the handle and wait for them in GetFlightInfo. This is how JDBC works, but not Python.
		// Therefore, we have to keep the server state to maintain statement handles.
		var result = new DoPutPreparedStatementResult {
			PreparedStatementHandle = statement.PreparedStatementHandle,
		};

		// WORKAROUND: CancellationToken is not supported by .NET implementation
		await response.WriteAsync(new(PackRaw(result)));
	}

	private static RpcException PreparedStatementNotFound()
		=> CreateException(StatusCode.NotFound, "Prepared statement handle doesn't exist");

	private Task<FlightInfo> GetPreparedStatementSchemaAsync(ByteString handle,
		ConnectionState state,
		FlightDescriptor descriptor,
		CancellationToken token) {
		Task<FlightInfo> task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled<FlightInfo>(token);
		} else {
			try {
				var ticket = PackToAny(new CommandPreparedStatementQuery { PreparedStatementHandle = handle });
				var ep = new FlightEndpoint(new FlightTicket(ticket), []);
				var schema = GetPreparedStatementSchema(handle.Span, state);
				task = Task.FromResult(new FlightInfo(schema, descriptor, [ep]));
			} catch (Exception e) {
				task = Task.FromException<FlightInfo>(e);
			}
		}

		return task;
	}

	private static Task<Schema> GetPreparedStatementSchemaAsync(ReadOnlyMemory<byte> handle, ConnectionState state, CancellationToken token) {
		Task<Schema> task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled<Schema>(token);
		} else {
			try {
				task = Task.FromResult(GetPreparedStatementSchema(handle.Span, state));
			} catch (Exception e) {
				task = Task.FromException<Schema>(e);
			}
		}

		return task;
	}

	private static Schema GetPreparedStatementSchema(ReadOnlySpan<byte> handle, ConnectionState state)
		=> state.TryGetPreparedStatementSchema(new(handle), out var schema)
			? schema
			: throw PreparedStatementNotFound();

	private Task ExecutePreparedStatementAsync(ReadOnlyMemory<byte> handle,
		ConnectionState state,
		FlightServerRecordBatchStreamWriter writer,
		CancellationToken token) {
		if (state.TryGetPreparedStatement(new(handle.Span)) is not { } statement)
			return Task.FromException(PreparedStatementNotFound());

		// if no parameters binding, execute the plain query
		return statement.TryUnbind() is { } bindingContext
			? ExecutePreparedStatementAsync(statement, bindingContext, writer, token)
			: ExecutePreparedStatementAsync(statement, writer, token);
	}

	// provided batch execution of the prepared statement for every row of the parameters in the binding context
	private async Task ExecutePreparedStatementAsync(
		PreparedStatement statement,
		RecordBatch bindingContext,
		FlightServerRecordBatchStreamWriter writer,
		CancellationToken token) {
		try {
			for (var i = 0; i < bindingContext.Length; i++) {
				await engine.ExecuteAsync<PreparedStatementResultConsumer>(
					statement.Query,
					new(statement, writer, bindingContext.Slice(i, bindingContext.Length - i)),
					new() { CheckIntegrity = false },
					token);
			}
		} finally {
			statement.DecrementRef();

			// return binding context back. Due to buggy client behavior, if it sends another binding,
			// we can replace it with the previous one again. In reality, it should never happen.
			if (!statement.TryBind(bindingContext, out var oldBinding))
				oldBinding = bindingContext;

			oldBinding?.Dispose();
		}
	}

	// execute the statement without binding parameters
	private async Task ExecutePreparedStatementAsync(
		PreparedStatement statement,
		FlightServerRecordBatchStreamWriter writer,
		CancellationToken token) {
		try {
			await engine.ExecuteAsync<SimplePreparedStatementResultConsumer>(
				statement.Query,
				new(statement, writer),
				new() { CheckIntegrity = false },
				token);
		} finally {
			statement.DecrementRef();
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly struct PreparedStatementResultConsumer(
		PreparedStatement statement,
		FlightRecordBatchStreamWriter writer,
		RecordBatch bindingContext)
		: IQueryResultConsumer {

		void IQueryResultConsumer.Bind<TBinder>(scoped TBinder binder) {
			for (var i = 0; i < bindingContext.ColumnCount; i++) {
				Bind(binder, bindingContext.Column(i), i + 1);
			}
		}

		private static void Bind<TBinder>(scoped TBinder binder, IArrowArray array, int position)
			where TBinder : IPreparedQueryBinder, allows ref struct {
			// If the first row is null, bind null
			if (array.IsNull(0)) {
				binder.Bind(position, ReadOnlySpan<byte>.Empty, ParameterType.Null);
				return;
			}

			// Otherwise, perform typed binding
			switch (array) {
				case Int8Array param:
					Bind(binder, param, position, ParameterType.Int8);
					break;
				case UInt8Array param:
					Bind(binder, param, position, ParameterType.UInt8);
					break;
				case Int32Array param:
					Bind(binder, param, position, ParameterType.Int32);
					break;
				case UInt32Array param:
					Bind(binder, param, position, ParameterType.UInt32);
					break;
				case Int64Array param:
					Bind(binder, param, position, ParameterType.Int64);
					break;
				case UInt64Array param:
					Bind(binder, param, position, ParameterType.UInt64);
					break;
				case BooleanArray param:
					var value = Unsafe.BitCast<bool, byte>(param.GetValue(0).GetValueOrDefault());
					binder.Bind(position, new(in value), ParameterType.Boolean);
					break;
				case StringArray param:
					binder.Bind(position, param.GetBytes(0), ParameterType.Utf8String);
					break;
				case StringViewArray param:
					binder.Bind(position, param.GetBytes(0), ParameterType.Utf8String);
					break;
				case LargeStringArray param:
					binder.Bind(position, param.GetBytes(0), ParameterType.Utf8String);
					break;
				case BinaryArray param:
					binder.Bind(position, param.GetBytes(0), ParameterType.Blob);
					break;
				case BinaryViewArray param:
					binder.Bind(position, param.GetBytes(0), ParameterType.Blob);
					break;
				case LargeBinaryArray param:
					binder.Bind(position, param.GetBytes(0), ParameterType.Blob);
					break;
				default:
					throw CreateException(StatusCode.Unimplemented, $"Array type {array.GetType().Name} is not supported");
			}
		}

		private static void Bind<TBinder, T>(scoped TBinder binder, PrimitiveArray<T> array, int position, ParameterType type)
			where TBinder : IPreparedQueryBinder, allows ref struct
			where T : unmanaged, IBinaryNumber<T>
			=> binder.Bind(position, MemoryMarshal.AsReadOnlyBytes(in array.Values[0]), type);

		ValueTask IQueryResultConsumer.ConsumeAsync(IQueryResultReader reader, CancellationToken token)
			=> ConsumeAsync(writer, statement.DatasetSchema, reader, token);

		public static async ValueTask ConsumeAsync(
			FlightRecordBatchStreamWriter writer,
			Schema schema,
			IQueryResultReader reader,
			CancellationToken token) {
			await writer.SetupStream(schema);

			using var options = reader.GetArrowOptions();
			while (reader.TryRead()) {
				using (var batch = reader.Chunk.ToRecordBatch(options, schema)) {
					await writer.WriteAsync(batch);
				}

				token.ThrowIfCancellationRequested();
			}
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly struct SimplePreparedStatementResultConsumer(
		PreparedStatement statement,
		FlightRecordBatchStreamWriter writer)
		: IQueryResultConsumer {

		ValueTask IQueryResultConsumer.ConsumeAsync(IQueryResultReader reader, CancellationToken token)
			=> PreparedStatementResultConsumer.ConsumeAsync(writer, statement.DatasetSchema, reader, token);
	}
}
