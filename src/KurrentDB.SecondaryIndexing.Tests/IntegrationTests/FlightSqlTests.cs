// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using Apache.Arrow.Adbc.Client;
using Apache.Arrow.Adbc.Drivers.FlightSql;
using Apache.Arrow.Ipc;
using KurrentDB.Surge.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class FlightSqlTests(IndexingFixture fixture, ITestOutputHelper output)
	: ClusterVNodeTests<IndexingFixture>(fixture, output){

	[Fact]
	public async Task QueryValues() {
		var urls = Fixture
			.NodeServices
			.GetRequiredService<IServer>()
			.Features
			.Get<IServerAddressesFeature>()
			?.Addresses ?? [];

		await Task.Delay(TimeSpan.FromMinutes(10));
		var port = new Uri(urls.First()).Port;
		using var driver = new FlightSqlDriver();

		var parameters = new Dictionary<string, string> {
			[FlightSqlParameters.ServerAddress] = $"http://localhost:{port}"
		};

		var options = new Dictionary<string, string> {
			[FlightSqlParameters.ServerAddress] = $"http://localhost:{port}"
		};

		await using var connection = new AdbcConnection(driver, parameters, options);

		await connection.OpenAsync(TestContext.Current.CancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT * FROM kdb.records";
		await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken)) {
			while (await reader.ReadAsync(TestContext.Current.CancellationToken)) {
				Assert.True(reader.HasRows);
				Assert.IsType<long>(reader["log_position"]);
			}
		}
	}
}
