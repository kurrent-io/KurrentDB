// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.XUnit.Tests;
using KurrentDB.SecondaryIndexing.LogsQuery;

namespace KurrentDB.SecondaryIndexing.Tests.LogsQuery;

// Exercises the LogViews glob-source builder directly on a (non-hardened) pool connection. The
// rewriter's job of splicing the built SQL into a query AST is covered separately.
public sealed class LogViewsTests : DirectoryPerTest<LogViewsTests> {
	private const string InfoLine =
		"""{"@t":"2026-07-06T09:28:00.6878711+00:00","@mt":"Current version of KurrentDB is : {dbVersion}","@l":"Information","@i":2803721463,"dbVersion":"26.1.0.3443","SourceContext":"Foo.Bar","ProcessId":7,"ThreadId":9}""";

	private const string ErrorLine =
		"""{"@t":"2026-07-06T09:28:01.0000000+00:00","@mt":"boom","@l":"Error","@i":1,"@x":"System.Exception: nope","ProcessId":1,"ThreadId":9}""";

	private const string StatsLine =
		"""{"@t":"2026-07-06T07:14:19.3286989+00:00","@mt":"{@stats}","@l":"Information","@i":3047155976,"stats":{"proc":{"cpu":0.5,"mem":1024}}}""";

	private readonly DuckDBConnectionPool _duckDb;
	private readonly string _logsDir;

	public LogViewsTests() {
		_logsDir = Path.Combine(Fixture.Directory, "component-logs");
		Directory.CreateDirectory(_logsDir);
		_duckDb = new($"Data Source={Fixture.GetFilePathFor("logs.db")};");
		using (_duckDb.Rent(out var connection))
			new RenderMessageFunction().Register(connection);
	}

	private void Write(string fileName, params string[] lines) =>
		File.WriteAllText(Path.Combine(_logsDir, fileName), string.Join('\n', lines) + "\n");

	private string Logs => new LogViews(_logsDir).BuildLogsSql();
	private string Stats => new LogViews(_logsDir).BuildStatsSql();

	[Fact]
	public void ReadsEveryLevel() {
		Write("log20260706.json", InfoLine, ErrorLine);
		Assert.Equal(2, ScalarLong($"SELECT count(*) FROM ({Logs})"));
	}

	[Fact]
	public void ExcludesErrorAndStatsFilesFromLogs() {
		Write("log20260706.json", InfoLine, ErrorLine);
		Write("log-err20260706.json", ErrorLine);
		Write("log-stats20260706.json", StatsLine);
		Assert.Equal(2, ScalarLong($"SELECT count(*) FROM ({Logs})"));
	}

	[Fact]
	public void ReadsUndatedMainLog() {
		// RollingInterval.Infinite writes a bare log.json with no date suffix; the glob must catch it.
		Write("log.json", InfoLine);
		Assert.Equal(1, ScalarLong($"SELECT count(*) FROM ({Logs})"));
	}

	[Fact]
	public void RendersMessageTemplate() {
		Write("log20260706.json", InfoLine);
		Assert.Equal("Current version of KurrentDB is : 26.1.0.3443",
			ScalarString($"SELECT message FROM ({Logs}) WHERE level = 'Information'"));
	}

	[Fact]
	public void ExtractsPlainColumns() {
		Write("log20260706.json", InfoLine);
		Assert.Equal("Foo.Bar", ScalarString($"SELECT source_context FROM ({Logs})"));
		Assert.Equal(7, ScalarLong($"SELECT process_id FROM ({Logs})"));
		Assert.Equal(9, ScalarLong($"SELECT thread_id FROM ({Logs})"));
		Assert.Equal("log20260706.json", ScalarString($"SELECT file FROM ({Logs})"));
	}

	[Fact]
	public void ExposesException() {
		Write("log20260706.json", ErrorLine);
		Assert.Equal("System.Exception: nope",
			ScalarString($"SELECT exception FROM ({Logs}) WHERE level = 'Error'"));
	}

	[Fact]
	public void NoFilesYieldsEmptyWithoutError() {
		Assert.Equal(0, ScalarLong($"SELECT count(*) FROM ({Logs})"));
		Assert.Equal(0, ScalarLong($"SELECT count(*) FROM ({Stats})"));
	}

	[Fact]
	public void StatsReadsFileAndExposesRawPayload() {
		Write("log-stats20260706.json", StatsLine);
		Assert.Equal(1, ScalarLong($"SELECT count(*) FROM ({Stats})"));
		Assert.Equal("0.5", ScalarString($"SELECT raw->'stats'->'proc'->>'cpu' FROM ({Stats})"));
	}

	private long ScalarLong(string sql) {
		using (_duckDb.Rent(out var connection)) {
			using var result = connection.ExecuteAdHocQuery(Encoding.UTF8.GetBytes(sql));
			while (result.TryFetch(out var chunk))
				using (chunk)
					if (chunk.TryRead(out var row))
						return row.ReadInt64();
		}

		throw new InvalidOperationException("query returned no rows");
	}

	private string ScalarString(string sql) {
		using (_duckDb.Rent(out var connection)) {
			using var result = connection.ExecuteAdHocQuery(Encoding.UTF8.GetBytes(sql));
			while (result.TryFetch(out var chunk))
				using (chunk)
					if (chunk.TryRead(out var row))
						return row.ReadString();
		}

		throw new InvalidOperationException("query returned no rows");
	}

	public override async ValueTask DisposeAsync() {
		_duckDb.Dispose();
		await base.DisposeAsync();
	}
}
