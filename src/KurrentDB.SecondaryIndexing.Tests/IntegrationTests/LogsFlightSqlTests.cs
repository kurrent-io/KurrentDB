// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow.Adbc.Client;
using Apache.Arrow.Adbc.Drivers.FlightSql;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Tests;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.Surge.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

// Points the node's log directory at a writable temp dir so these tests can seed CLEF files
// (the default /var/log/kurrentdb is not writable in the test environment). The running node
// also writes its own logs there; seeded rows are isolated by unique markers.
public sealed class LogsQueryFixture : SecondaryIndexingEnabledFixture {
	public LogsQueryFixture() {
		var logsRoot = Path.Combine(Path.GetTempPath(), $"logs-query-{Guid.NewGuid():N}");
		Directory.CreateDirectory(logsRoot);
		Configuration = new Dictionary<string, string?>(Configuration!) {
			[$"{KurrentConfigurationKeys.Prefix}:Logging:Log"] = logsRoot,
		};

		var baseTearDown = OnTearDown;
		OnTearDown = async () => {
			await baseTearDown();
			await DirectoryDeleter.TryForceDeleteDirectoryAsync(logsRoot, retries: 10);
		};
	}
}

[Trait("Category", "Integration")]
public class LogsFlightSqlTests(LogsQueryFixture fixture, ITestOutputHelper output)
	: ClusterVNodeTests<LogsQueryFixture>(fixture, output) {

	[Fact]
	public async Task QueriesSeededLogsRow() {
		var marker = $"log-marker-{Guid.NewGuid():N}";
		Seed("log20200101.json",
			$$"""{"@t":"2020-01-01T00:00:00.0000000+00:00","@mt":"{{marker}}","@l":"Warning","@i":42,"ProcessId":1,"ThreadId":9}""");

		Assert.Equal("Warning", await QueryStringAsync($"SELECT level FROM kdb.logs WHERE message = '{marker}'"));
		Assert.Equal(marker, await QueryStringAsync($"SELECT message FROM kdb.logs WHERE message = '{marker}'"));
	}

	[Fact]
	public async Task QueriesSeededStatsRow() {
		Seed("log-stats20200101.json",
			"""{"@t":"2020-01-01T00:00:00.0000000+00:00","@mt":"{@stats}","@l":"Information","@i":987654321,"stats":{"proc":{"cpu":0.5}}}""");

		Assert.Equal(1, await QueryCountAsync("SELECT count(*) FROM kdb.stats WHERE event_id = 987654321"));
		Assert.Equal("0.5", await QueryStringAsync("SELECT raw->'stats'->'proc'->>'cpu' FROM kdb.stats WHERE event_id = 987654321"));
	}

	// One query touching both kdb.logs and kdb.stats - both views must be created on the exec connection.
	[Fact]
	public async Task QueriesAcrossLogsAndStatsViews() {
		var marker = $"union-{Guid.NewGuid():N}";
		Seed("log20200102.json", $$"""{"@t":"2020-01-02T00:00:00.0000000+00:00","@mt":"{{marker}}","@l":"Information","@i":1}""");
		Seed("log-stats20200102.json", $$"""{"@t":"2020-01-02T00:00:00.0000000+00:00","@mt":"{@stats}","@l":"Information","@i":555000001}""");

		Assert.Equal(2, await QueryCountAsync(
			$"SELECT count(*) FROM (SELECT message FROM kdb.logs WHERE message = '{marker}' " +
			"UNION ALL SELECT message_template FROM kdb.stats WHERE event_id = 555000001)"));
	}

	[Fact]
	public async Task RejectsUnknownKdbTable() =>
		await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync("SELECT * FROM kdb.nope"));

	[Fact]
	public async Task RejectsUnqualifiedPhysicalView() =>
		await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync("SELECT * FROM __logs"));

	[Fact]
	public async Task TimeBoundedQueryReturnsOnlyInWindowRows() {
		var oldMarker = $"old-{Guid.NewGuid():N}";
		var newMarker = $"new-{Guid.NewGuid():N}";
		Seed("log20200101.json", $$"""{"@t":"2020-01-01T00:00:00.0000000+00:00","@mt":"{{oldMarker}}","@l":"Information","@i":1}""");
		Seed("log20300101.json", $$"""{"@t":"2030-01-01T00:00:00.0000000+00:00","@mt":"{{newMarker}}","@l":"Information","@i":2}""");

		Assert.Equal(newMarker, await QueryStringAsync(
			$"SELECT message FROM kdb.logs WHERE timestamp >= TIMESTAMP '2030-01-01' AND message IN ('{oldMarker}', '{newMarker}')"));
		Assert.Equal(0, await QueryCountAsync(
			$"SELECT count(*) FROM kdb.logs WHERE timestamp >= TIMESTAMP '2030-01-01' AND message = '{oldMarker}'"));
	}

	// Non-SELECT statements are rejected at parse (json_serialize_sql is SELECT-only), so the
	// surface is read-only for statement types - no CTAS / COPY-write / ATTACH / DML.
	[Fact]
	public async Task RejectsCreateTableAsSelect() =>
		await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync("CREATE TABLE t AS SELECT * FROM kdb.logs"));

	[Fact]
	public async Task RejectsCopyToFileWrite() =>
		await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync("COPY (SELECT 1) TO '/tmp/x.csv' (FORMAT CSV)"));

	private void Seed(string fileName, string clefLine) {
		var dir = Path.Combine(Fixture.NodeOptions.Logging.Log, Fixture.NodeOptions.GetComponentName());
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, fileName), clefLine + "\n");
	}

	private async Task<string?> QueryStringAsync(string sql) {
		await foreach (var value in QueryFirstColumnAsync(sql))
			return value?.ToString();

		return null;
	}

	private async Task<long> QueryCountAsync(string sql) {
		await foreach (var value in QueryFirstColumnAsync(sql))
			return Convert.ToInt64(value);

		return 0;
	}

	private async Task ExecuteAsync(string sql) {
		await foreach (var _ in QueryFirstColumnAsync(sql)) { }
	}

	private async IAsyncEnumerable<object?> QueryFirstColumnAsync(string sql) {
		using var driver = new FlightSqlDriver();
		var parameters = new Dictionary<string, string> { [FlightSqlParameters.ServerAddress] = HostingAddress };
		await using var connection = new AdbcConnection(driver, parameters, parameters);
		await connection.OpenAsync(TestContext.Current.CancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
		while (await reader.ReadAsync(TestContext.Current.CancellationToken))
			yield return reader[0];
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
}
