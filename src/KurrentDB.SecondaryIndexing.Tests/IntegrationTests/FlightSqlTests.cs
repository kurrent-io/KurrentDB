// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using Apache.Arrow.Adbc.Client;
using Apache.Arrow.Adbc.Drivers.FlightSql;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Client;
using Apache.Arrow.Flight.Sql;
using Apache.Arrow.Flight.Sql.Client;
using Apache.Arrow.Ipc;
using Arrow.Flight.Protocol.Sql;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using KurrentDB.Surge.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class FlightSqlTests(IndexingFixture fixture, ITestOutputHelper output)
	: ClusterVNodeTests<IndexingFixture>(fixture, output){

	[Fact]
	public async Task AdoNetDriver() {
		using var driver = new FlightSqlDriver();

		var parameters = new Dictionary<string, string> {
			[FlightSqlParameters.ServerAddress] = HostingAddress
		};

		var options = new Dictionary<string, string> {
			[FlightSqlParameters.ServerAddress] = HostingAddress
		};

		await using var connection = new AdbcConnection(driver, parameters, options);

		await connection.OpenAsync(TestContext.Current.CancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT * FROM kdb.records";
		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
		while (await reader.ReadAsync(TestContext.Current.CancellationToken)) {
			Assert.True(reader.HasRows);
			Assert.IsType<long>(reader["log_position"]);
		}
	}

	[Fact]
	public async Task PreparedStatementWorkflow() {
		// This test reproduces approximate JDBC/Arrow and PyArrow behavior
		using var channel = GrpcChannel.ForAddress(HostingAddress);
		var client = new FlightSqlClient(new FlightClient(channel));

		// Create prepared statement
		var createStatementAction = new ActionCreatePreparedStatementRequest() {
			Query = "SELECT * FROM kdb.records WHERE log_position > ?",
		};

		var handle = ByteString.Empty;
		await foreach (var response in client.DoActionAsync(
			               new FlightAction(SqlAction.CreateRequest, Any.Pack(createStatementAction).ToByteString()),
			               cancellationToken: TestContext.Current.CancellationToken)) {
			var result = FlightSqlUtils.ParseAndUnpack<ActionCreatePreparedStatementResult>(response.Body);
			handle = result.PreparedStatementHandle;

			// check Dataset schema
			var schema = await DeserializeSchemaAsync(result.DatasetSchema);
			Assert.NotNull(schema["log_position"]);

			// check parameters
			schema = await DeserializeSchemaAsync(result.ParameterSchema);
			Assert.Single(schema.FieldsList);
		}

		// Bind prepared statement
		using var arguments = new RecordBatch.Builder()
			.Append("1", false, new Int64Array.Builder().Append(42L).Build())
			.Build();

		var infoRequest = new CommandPreparedStatementQuery {
			PreparedStatementHandle = handle,
		};

		var putResult = await client.DoPutAsync(
			FlightDescriptor.CreateCommandDescriptor(Any.Pack(infoRequest).ToByteArray()),
			arguments,
			cancellationToken: TestContext.Current.CancellationToken);

		// In our implementation we return the same prepared statement handle on the bounded statement
		var putResponse = DoPutPreparedStatementResult.Parser.ParseFrom(putResult.ApplicationMetadata);
		Assert.Equal(handle, putResponse.PreparedStatementHandle);

		// GetFlightInfo
		var infoResponse = await client.GetFlightInfoAsync(
			FlightDescriptor.CreateCommandDescriptor(Any.Pack(infoRequest).ToByteArray()),
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.NotNull(infoResponse.Schema["log_position"]);
		var ticket = Assert.Single(infoResponse.Endpoints).Ticket;

		// Execute prepared statement
		await foreach (var response in client.DoGetAsync(ticket, cancellationToken: TestContext.Current.CancellationToken)) {
			Assert.NotNull(response.Schema["log_position"]);

			Assert.NotNull(response.Column("log_position"));
			response.Dispose();
		}

		// Close prepared statement
		var closeRequest = new ActionClosePreparedStatementRequest {
			PreparedStatementHandle = handle
		};

		await foreach (var response in client.DoActionAsync(
			               new FlightAction(SqlAction.CloseRequest, Any.Pack(closeRequest).ToByteString()),
			               cancellationToken: TestContext.Current.CancellationToken)) {
			Assert.Empty(response.Body);
		}
	}

	private string HostingAddress {
		get {
			var urls = Fixture
				.NodeServices
				.GetRequiredService<IServer>()
				.Features
				.Get<IServerAddressesFeature>()
				?.Addresses ?? [];

			var port = new Uri(urls.First()).Port;
			return $"http://localhost:{port}";
		}
	}

	private static async Task<Schema> DeserializeSchemaAsync(ByteString data) {
		using var reader = new ArrowStreamReader(data.Memory);
		return await reader.GetSchema(TestContext.Current.CancellationToken);
	}
}
