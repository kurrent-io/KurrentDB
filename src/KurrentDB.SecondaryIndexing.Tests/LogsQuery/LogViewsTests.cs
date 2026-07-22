// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.XUnit.Tests;
using KurrentDB.SecondaryIndexing.LogsQuery;

namespace KurrentDB.SecondaryIndexing.Tests.LogsQuery;

public sealed class LogViewsTests : DirectoryPerTest<LogViewsTests> {
	private const string InfoLine =
		"""{"@t":"2026-07-06T09:28:00.6878711+00:00","@mt":"Current version of KurrentDB is : {dbVersion}","@l":"Information","@i":2803721463,"dbVersion":"26.1.0.3443","ProcessId":1,"ThreadId":148}""";

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

	[Fact]
	public void ReadsEveryLevel() {
		Write("log20260706.json", InfoLine, ErrorLine);
		Assert.Equal(2, ScalarLong("select count(*) from __logs"u8));
	}

	[Fact]
	public void ErrorsAreALevelSubsetOfLogs() {
		Write("log20260706.json", InfoLine, ErrorLine);
		Assert.Equal(1, ScalarLong("select count(*) from __logs where level in ('Error', 'Fatal')"u8));
	}

	[Fact]
	public void RendersMessageTemplate() {
		Write("log20260706.json", InfoLine);
		Assert.Equal("Current version of KurrentDB is : 26.1.0.3443",
			ScalarString("select message from __logs where level = 'Information'"u8));
	}

	[Fact]
	public void ExposesException() {
		Write("log20260706.json", ErrorLine);
		Assert.Equal("System.Exception: nope", ScalarString("select exception from __logs where level = 'Error'"u8));
	}

	[Fact]
	public void DefaultsMissingLevelToInformation() {
		Write("log20260706.json", """{"@t":"2026-07-06T09:28:00.0000000+00:00","@mt":"no level","@i":7}""");
		Assert.Equal("Information", ScalarString("select level from __logs"u8));
	}

	[Fact]
	public void UnparseableTimestampBecomesNullNotError() {
		Write("log20260706.json", """{"@t":"not-a-timestamp","@mt":"bad ts","@l":"Warning","@i":8}""");
		Assert.Equal(1, ScalarLong("select count(*) from __logs where timestamp is null"u8));
	}

	[Fact]
	public void ExcludesErrorAndStatsFilesFromLogs() {
		Write("log20260706.json", InfoLine, ErrorLine);
		Write("log-err20260706.json", ErrorLine);
		Write("log-stats20260706.json", StatsLine);
		Assert.Equal(2, ScalarLong("select count(*) from __logs"u8));
	}

	[Fact]
	public void ReadsMultipleMainLogFiles() {
		Write("log20260706.json", InfoLine);
		Write("log20260707.json", ErrorLine);
		Assert.Equal(2, ScalarLong("select count(*) from __logs"u8));
	}

	[Fact]
	public void ReadsUndatedMainLog() {
		// RollingInterval.Infinite writes a bare log.json with no date suffix.
		Write("log.json", InfoLine);
		Assert.Equal(1, ScalarLong("select count(*) from __logs"u8));
	}

	[Fact]
	public void StatsViewReadsFileAndExposesRawPayload() {
		Write("log-stats20260706.json", StatsLine);
		Assert.Equal(1, ScalarLong("select count(*) from __stats"u8));
		Assert.Equal("0.5", ScalarString("select raw->'stats'->'proc'->>'cpu' from __stats"u8));
	}

	[Fact]
	public void NoFilesYieldsEmptyViewsWithoutError() {
		Assert.Equal(0, ScalarLong("select count(*) from __logs"u8));
		Assert.Equal(0, ScalarLong("select count(*) from __stats"u8));
	}

	[Fact]
	public void RendersFormatSpecifierFromRenderings() {
		Write("log20260706.json",
			"""{"@t":"2026-07-06T00:00:00.0000000+00:00","@mt":"count {n:N0}","@r":["1,234"],"@l":"Information","@i":1,"n":1234}""");
		Assert.Equal("count 1,234", ScalarString("select message from __logs"u8));
	}

	[Fact]
	public void DoesNotCollidePrefixSharingProperties() {
		Write("log20260706.json",
			"""{"@t":"2026-07-06T00:00:00.0000000+00:00","@mt":"{count} of {countTotal}","@l":"Information","@i":1,"count":3,"countTotal":10}""");
		Assert.Equal("3 of 10", ScalarString("select message from __logs"u8));
	}

	[Fact]
	public void UnescapesDoubledBraces() {
		Write("log20260706.json",
			"""{"@t":"2026-07-06T00:00:00.0000000+00:00","@mt":"literal {{brace}} then {x}","@l":"Information","@i":1,"x":"v"}""");
		Assert.Equal("literal {brace} then v", ScalarString("select message from __logs"u8));
	}

	[Fact]
	public void AppliesAlignment() {
		Write("log20260706.json",
			"""{"@t":"2026-07-06T00:00:00.0000000+00:00","@mt":"{x,-5}|","@l":"Information","@i":1,"x":"ab"}""");
		Assert.Equal("ab   |", ScalarString("select message from __logs"u8));
	}

	[Fact]
	public void RendersUnresolvedPropertyAsToken() {
		Write("log20260706.json", """{"@t":"2026-07-06T00:00:00.0000000+00:00","@mt":"hi {missing}","@l":"Information","@i":1}""");
		Assert.Equal("hi {missing}", ScalarString("select message from __logs"u8));
	}

	[Fact]
	public void FallsBackToRawValueWhenRenderingNotAString() {
		// @r element is a number, not a string - ignore it and render from the property (no format).
		Write("log20260706.json",
			"""{"@t":"2026-07-06T00:00:00.0000000+00:00","@mt":"count {n:N0}","@r":[123],"@l":"Information","@i":1,"n":5}""");
		Assert.Equal("count 5", ScalarString("select message from __logs"u8));
	}

	[Fact]
	public void ExtractsPlainColumns() {
		Write("log20260706.json",
			"""{"@t":"2026-07-06T09:28:00.6878711+00:00","@mt":"msg","@l":"Warning","@i":42,"SourceContext":"Foo.Bar","ProcessId":7,"ThreadId":9}""");
		Assert.Equal("msg", ScalarString("select message_template from __logs"u8));
		Assert.Equal("Foo.Bar", ScalarString("select source_context from __logs"u8));
		Assert.Equal(7, ScalarLong("select process_id from __logs"u8));
		Assert.Equal(9, ScalarLong("select thread_id from __logs"u8));
		Assert.Equal("log20260706.json", ScalarString("select file from __logs"u8));
	}

	private long ScalarLong(ReadOnlySpan<byte> sql) {
		using (_duckDb.Rent(out var connection)) {
			new LogViews(_logsDir).Create(connection, logs: true, stats: true);
			using var result = connection.ExecuteAdHocQuery(sql);
			while (result.TryFetch(out var chunk))
				using (chunk)
					if (chunk.TryRead(out var row))
						return row.ReadInt64();
		}

		throw new InvalidOperationException("query returned no rows");
	}

	private string ScalarString(ReadOnlySpan<byte> sql) {
		using (_duckDb.Rent(out var connection)) {
			new LogViews(_logsDir).Create(connection, logs: true, stats: true);
			using var result = connection.ExecuteAdHocQuery(sql);
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
