// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Order;
using KurrentDB.Duck;

namespace KurrentDB.MicroBenchmarks.DuckDB;

[SimpleJob(runStrategy: RunStrategy.Throughput, launchCount: 1)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[MemoryDiagnoser]
public class PreparedQueryBenchmark {
	private const string Query = "SELECT * FROM test_table WHERE col0 > 100;";
	private static ReadOnlySpan<byte> QueryUtf8 => "SELECT * FROM test_table WHERE col0 > 990;"u8;

	private DuckDBAdvancedConnection _connection;

	[GlobalSetup]
	public void SetupConnection() {
		var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		_connection = new() { ConnectionString = $"Data Source={path};" };
		_connection.Open();

		// create table and insert rows
		const string tableDefinition = """
		                               create table if not exists test_table (
		                                   col0 INTEGER not null primary key,
		                                   col1 INTEGER not null,
		                               );
		                               """;

		using (var command = _connection.CreateCommand()) {
			command.CommandText = tableDefinition;
			command.ExecuteNonQuery();
		}

		// compile the query
		_connection.GetPreparedStatement<PreparedQuery>();

		using var appender = new Appender(_connection, "test_table"u8);

		for (int i = 0; i < 1000; i++) {
			using var row = appender.CreateRow();
			row.Append(i);
			row.Append(i);
		}

		appender.Flush();
	}

	[GlobalCleanup]
	public void DestroyConnection() {
		_connection.Dispose();
	}

	[Benchmark]
	public void QueryWithoutPreparedStatement() {
		using var command = _connection.CreateCommand();
		command.CommandText = Query;

		using var reader = command.ExecuteReader();

		while (reader.Read()) {
			reader.GetInt32(0);
			reader.GetInt32(1);
		}
	}

	[Benchmark(Baseline = true)]
	public void QueryWithPreparedStatement() {
		using var result = _connection.ExecuteQuery<(int, int), PreparedQuery>().GetEnumerator();

		while (result.MoveNext()) {
		}
	}

	private struct PreparedQuery : IParameterlessStatement, IDataRowParser<(int, int)> {
		public static ReadOnlySpan<byte> CommandText => QueryUtf8;

		public static (int, int) Parse(ref DataChunk.Row row)
			=> (row.ReadInt32(), row.ReadInt32());
	}
}
