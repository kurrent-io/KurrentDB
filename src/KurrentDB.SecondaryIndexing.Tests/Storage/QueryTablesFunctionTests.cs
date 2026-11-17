// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using KurrentDB.SecondaryIndexing.Query;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Tests.Storage;

public sealed class QueryTablesFunctionTests {
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(5000)] // spans multiple data chunks
	public void ReturnsAllTablesSorted(int userTablesCount) {
		var tables = Enumerable.Range(0, userTablesCount)
			.Select(static i => new TableInfo("usr", $"index-{i}"))
			.Append(new("kdb", "records"))
			.ToArray();

		using var connection = new DuckDBConnection("Data Source=:memory:");
		connection.Open();
		new QueryTablesFunction(new StubSchemaProvider(tables)).Register(connection);

		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT * FROM {QueryTablesFunction.Name}()";
		using var reader = command.ExecuteReader();

		var actual = new List<string>();
		while (reader.Read())
			actual.Add(reader.GetString(0));

		var expected = tables.Select(static t => t.FullName).Order(StringComparer.Ordinal);
		Assert.Equal(expected, actual);
	}

	private sealed class StubSchemaProvider(IReadOnlyList<TableInfo> tables) : IDatabaseSchemaProvider {
		IEnumerable<TableInfo> IDatabaseSchemaProvider.Tables => tables;

		Apache.Arrow.Schema IDatabaseSchemaProvider.GetTableSchema(TableInfo table) => throw new NotSupportedException();
	}
}
