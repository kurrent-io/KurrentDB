// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using Kurrent.Quack;
using KurrentDB.Core;
using KurrentDB.Protocol.V2.Logs;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;
using Value = Google.Protobuf.WellKnownTypes.Value;

namespace KurrentDB.Plugins.LogsQuery;

internal sealed class LogsQueryService(
	LogsQueryDatabase db,
	IAuthorizationProvider authProvider,
	LogsQueryLicense license,
	ClusterVNodeOptions nodeOptions
) : Protocol.V2.Logs.LogsQueryService.LogsQueryServiceBase {
	const int BatchSize = 500;

	// 64-bit integer columns are serialized as strings: protobuf Value numbers are
	// IEEE doubles and lose precision above 2^53.
	static readonly HashSet<string> StringifyTypes = new(StringComparer.OrdinalIgnoreCase) {
		"BIGINT", "UBIGINT", "HUGEINT", "UHUGEINT"
	};

	static readonly Operation ReadLogsOperation = new(Operations.Node.Information.ReadLogs);

	public override async Task Execute(
		ExecuteRequest request,
		IServerStreamWriter<ExecuteResponse> responseStream,
		ServerCallContext context) {

		EnsureLicensed();
		await EnsureAccess(context);

		var ct = context.CancellationToken;
		var sql = request.Sql.Trim().TrimEnd(';');

		List<(string Name, string Type)> columns;
		List<Row> rows;
		try {
			using var _ = db.Rent(out var connection);
			// DESCRIBE both validates the statement (non-queries fail to parse as a
			// subquery source) and yields the result schema for the header.
			columns = ReadColumns(connection, $"SELECT to_json(d) FROM (DESCRIBE {sql}) d", ct);
			var bigints = columns.Where(c => StringifyTypes.Contains(c.Type)).Select(c => c.Name).ToHashSet();
			rows = ReadRows(connection, $"SELECT to_json(sub) FROM ({sql}) sub", bigints, ct);
		} catch (DuckDBException ex) when (ex.ErrorType is DuckDBErrorType.Interrupt) {
			throw new OperationCanceledException(ct);
		} catch (DuckDBException ex) when (IsNoFilesYet(ex)) {
			await responseStream.WriteAsync(new ExecuteResponse { Header = NewHeader([]) }, ct);
			return;
		} catch (DuckDBException ex) {
			throw ToRpcException(ex);
		}

		await responseStream.WriteAsync(new ExecuteResponse { Header = NewHeader(columns) }, ct);

		for (var i = 0; i < rows.Count; i += BatchSize) {
			var batch = new RowBatch();
			batch.Rows.AddRange(rows.GetRange(i, Math.Min(BatchSize, rows.Count - i)));
			await responseStream.WriteAsync(new ExecuteResponse { Batch = batch }, ct);
		}
	}

	static readonly string[] ViewNames = ["logs", "errors", "stats"];

	public override async Task<GetLogsInfoResponse> GetLogsInfo(GetLogsInfoRequest request, ServerCallContext context) {
		EnsureLicensed();
		await EnsureAccess(context);

		var ct = context.CancellationToken;
		var response = new GetLogsInfoResponse { Node = nodeOptions.GetComponentName() };
		try {
			using var _ = db.Rent(out var connection);

			foreach (var name in ViewNames) {
				var view = new LogsView { Name = name };
				// A view whose files don't exist yet (no errors/stats on a fresh node)
				// reports no columns rather than failing the whole call.
				try {
					foreach (var (colName, type) in ReadColumns(connection, $"SELECT to_json(d) FROM (DESCRIBE SELECT * FROM {name}) d", ct))
						view.Columns.Add(new Column { Name = colName, Type = type });
				} catch (DuckDBException ex) when (IsNoFilesYet(ex)) { }
				response.Views.Add(view);
			}

			try {
				ForEachJsonRow(connection,
					"SELECT to_json(t) FROM (SELECT epoch_ms(min(timestamp)) lo, epoch_ms(max(timestamp)) hi FROM logs) t",
					ct, e => {
						if (e.TryGetProperty("lo", out var lo) && lo.ValueKind is JsonValueKind.Number)
							response.Earliest = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(lo.GetInt64()));
						if (e.TryGetProperty("hi", out var hi) && hi.ValueKind is JsonValueKind.Number)
							response.Latest = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(hi.GetInt64()));
					});
			} catch (DuckDBException ex) when (IsNoFilesYet(ex)) { }
		} catch (DuckDBException ex) {
			throw ToRpcException(ex);
		}

		return response;
	}

	Header NewHeader(IReadOnlyList<(string Name, string Type)> columns) {
		var header = new Header { Node = nodeOptions.GetComponentName() };
		foreach (var (name, type) in columns)
			header.Columns.Add(new Column { Name = name, Type = type });
		return header;
	}

	static List<(string, string)> ReadColumns(DuckDBAdvancedConnection connection, string sql, CancellationToken ct) {
		var columns = new List<(string, string)>();
		ForEachJsonRow(connection, sql, ct, e =>
			columns.Add((e.GetProperty("column_name").GetString()!, e.GetProperty("column_type").GetString()!)));
		return columns;
	}

	static List<Row> ReadRows(DuckDBAdvancedConnection connection, string sql, HashSet<string> bigints, CancellationToken ct) {
		var rows = new List<Row>();
		ForEachJsonRow(connection, sql, ct, e => {
			var row = new Row();
			foreach (var p in e.EnumerateObject())
				row.Fields[p.Name] = ToValue(p.Value, bigints.Contains(p.Name));
			rows.Add(row);
		});
		return rows;
	}

	// Runs a query whose single column is a JSON string per row and invokes onRow for
	// each. Materializes into the caller's collection; streaming straight to the wire
	// is a follow-up (queries are LIMIT-bounded in practice).
	static void ForEachJsonRow(DuckDBAdvancedConnection connection, string sql, CancellationToken ct, Action<JsonElement> onRow) {
		using var registration = connection.InterruptQueryOnCancellation(ct);
		using var statement = new PreparedStatement(connection, sql.AsSpan());
		using var result = statement.ExecuteQuery(useStreaming: true);

		var chunk = default(DataChunk);
		try {
			while (true) {
				chunk.Dispose();
				if (!result.TryFetch(out chunk))
					break;
				foreach (ref readonly var cell in chunk[0].BlobRows) {
					ct.ThrowIfCancellationRequested();
					using var doc = JsonDocument.Parse(cell.AsSpan().ToArray());
					onRow(doc.RootElement);
				}
			}
		} finally {
			chunk.Dispose();
		}
	}

	static Value ToValue(JsonElement e, bool asString) {
		if (e.ValueKind is JsonValueKind.Null)
			return Value.ForNull();
		if (asString)
			return Value.ForString(e.ValueKind is JsonValueKind.String ? e.GetString()! : e.GetRawText());
		return e.ValueKind switch {
			JsonValueKind.Number => Value.ForNumber(e.GetDouble()),
			JsonValueKind.String => Value.ForString(e.GetString()!),
			JsonValueKind.True or JsonValueKind.False => Value.ForBool(e.GetBoolean()),
			JsonValueKind.Object or JsonValueKind.Array => Value.ForString(e.GetRawText()),
			_ => Value.ForNull()
		};
	}

	static bool IsNoFilesYet(DuckDBException ex) =>
		ex.ErrorType is DuckDBErrorType.Io && ex.Message.Contains("No files found", StringComparison.OrdinalIgnoreCase);

	// DuckDB.NET leaves ErrorType == UnknownType for prepare-time failures, so classify
	// by the message's error-class prefix. Production could read the native result error
	// type instead. AST-based statement classification would also yield a friendlier
	// "only read queries are supported" than a raw syntax error for a rejected COPY.
	static readonly string[] UserErrorPrefixes = [
		"Parser Error", "Binder Error", "Catalog Error", "Conversion Error",
		"Invalid Input Error", "Out of Range Error", "Not implemented Error"
	];

	static RpcException ToRpcException(DuckDBException ex) {
		if (ex.Message.StartsWith("Permission Error", StringComparison.Ordinal))
			return new(new Status(StatusCode.InvalidArgument,
				"Filesystem access outside the log directory is disabled."));
		if (UserErrorPrefixes.Any(p => ex.Message.StartsWith(p, StringComparison.Ordinal)))
			return new(new Status(StatusCode.InvalidArgument, ex.Message));
		return new(new Status(StatusCode.Internal, ex.Message));
	}

	async Task EnsureAccess(ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await authProvider.CheckAccessAsync(user, ReadLogsOperation, context.CancellationToken))
			throw new RpcException(new Status(StatusCode.PermissionDenied, "Access denied"));
	}

	void EnsureLicensed() {
		if (!license.IsLicensed)
			throw new RpcException(new Status(StatusCode.FailedPrecondition,
				$"Logs query is not licensed. Required entitlement: {LogsQueryLicense.Entitlement}"));
	}
}
