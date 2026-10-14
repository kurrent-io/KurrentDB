// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using Apache.Arrow.Flight.Client;
using Apache.Arrow.Flight.Sql.Client;
using Grpc.Core;
using Serilog;

namespace KurrentDB.Embedded.Sample;

/// <summary>
/// Querying the secondary index with Arrow Flight SQL, which the node serves over the same socket as
/// everything else.
/// </summary>
/// <remarks>
/// The usual Flight SQL drivers — ADBC, JDBC, PyArrow — are configured with a URL, and no URL names a UNIX
/// domain socket, so none of them can reach an embedded node. <c>FlightSqlClient</c> is built on a channel
/// rather than an address, which is what makes this work.
/// </remarks>
internal sealed class FlightSqlSamples(EmbeddedKurrentDB db) {
	const string Query = """
		SELECT * FROM kdb.records LIMIT 10;
		""";

	public async Task RunAsync(CancellationToken cancellationToken) {
		using var channel = db.CreateChannel();
		var client = new FlightSqlClient(new FlightClient(channel));

		Log.Information("Querying over Flight SQL: {query}", Query);

		try {
			var info = await client.ExecuteAsync(Query, cancellationToken: cancellationToken);

			// the endpoints carry no location, which is the server saying the rows come back over this same
			// connection rather than from an address the client would have to dial
			foreach (var endpoint in info.Endpoints) {
				await foreach (var batch in client.DoGetAsync(endpoint.Ticket, cancellationToken: cancellationToken)) {
					using (batch) {
						for (var row = 0; row < batch.Length; row++) {
							var columns = batch.Schema.FieldsList.Select(
								(field, column) => $"{field.Name}={Format(batch.Column(column), row)}");

							Log.Information("  {row}", string.Join(" ", columns));
						}
					}
				}
			}
		} catch (RpcException ex) when (ex.StatusCode is StatusCode.FailedPrecondition) {
			// the node was reached and the feature refused: Flight SQL is licensed separately
			Log.Warning("  Flight SQL is not available: {detail}", ex.Status.Detail);
		}
	}

	static string Format(IArrowArray column, int row) => column switch {
		StringArray text => text.GetString(row) ?? "",
		Int64Array numbers => numbers.GetValue(row)?.ToString() ?? "",
		_ => $"<{column.Data.DataType.Name}>",
	};
}
