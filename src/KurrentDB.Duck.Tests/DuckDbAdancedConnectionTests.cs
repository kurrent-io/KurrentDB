// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit;

namespace KurrentDB.Duck.Tests;

public sealed class DuckDbAdancedConnectionTests : DuckDbTests<DuckDbAdancedConnectionTests> {
	[Fact]
	public void InsertValues() {
		using var connection = new DuckDbAdvancedConnection { ConnectionString = ConnectionString };
		connection.Open();

		connection.ExecuteNonQuery<TableDefinitionStatement>();
		connection.ExecuteNonQuery<(uint, string), InsertStatement>((0U, "A"));
		connection.ExecuteNonQuery<(uint, string), InsertStatement>((1U, "B"));

		using (var command = connection.CreateCommand()) {
			command.CommandText = "SELECT * FROM test_table;";

			using var reader = command.ExecuteReader();

			Assert.True(reader.Read());
			Assert.Equal(0U, reader.GetValue(0));
			Assert.Equal("A", reader.GetValue(1));

			Assert.True(reader.Read());
			Assert.Equal(1U, reader.GetValue(0));
			Assert.Equal("B", reader.GetValue(1));

			Assert.False(reader.Read());
		}
	}

	private struct TableDefinitionStatement : IParameterlessStatement {
		public static ReadOnlySpan<byte> CommandText => """
		                                                create table if not exists test_table (
		                                                    col0 UINTEGER primary key,
		                                                    col1 VARCHAR,
		                                                );
		                                                """u8;
	}

	private struct InsertStatement: IPreparedStatement<(uint, string)> {
		public static BindingContext Bind(ref (uint, string) args, BindingSource source) => new(source) {
			args.Item1,
			args.Item2
		};

		public static ReadOnlySpan<byte> CommandText => "INSERT INTO test_table VALUES ($1, $2);"u8;
	}
}
