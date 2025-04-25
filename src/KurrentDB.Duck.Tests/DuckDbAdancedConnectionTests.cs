// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit;

namespace KurrentDB.Duck.Tests;

public sealed class DuckDbAdancedConnectionTests : DuckDbTests<DuckDbAdancedConnectionTests> {
	[Fact]
	public void InsertValues() {
		using var connection = new DuckDbAdvancedConnection { ConnectionString = ConnectionString };
		connection.Open();

		Assert.Equal(0L, connection.ExecuteNonQuery<NotNullTableDefinition>());

		const uint count = 1000;
		uint expectedCount;

		for (expectedCount = 0; expectedCount < count; expectedCount++) {
			Assert.Equal(1L, connection.ExecuteNonQuery<(uint, string?), InsertStatement>((expectedCount, expectedCount.ToString())));
		}

		uint actualCount = 0U;

		foreach (var row in connection.ExecuteQuery<(uint, string), QueryStatement>()) {
			Assert.Equal((actualCount, actualCount.ToString()), row);
			actualCount++;
		}

		Assert.Equal(expectedCount, actualCount);
	}

	[Fact]
	public void InsertNullValues() {
		using var connection = new DuckDbAdvancedConnection { ConnectionString = ConnectionString };
		connection.Open();

		Assert.Equal(0L, connection.ExecuteNonQuery<NullableTableDefinition>());
		Assert.Equal(1L, connection.ExecuteNonQuery<(uint, string?), InsertStatement>((0U, "A")));
		Assert.Equal(1L, connection.ExecuteNonQuery<(uint, string?), InsertStatement>((1U, null)));

		using var enumerator = connection.ExecuteQuery<(uint, string?), NullableQueryStatement>().GetEnumerator();

		Assert.True(enumerator.MoveNext());
		Assert.Equal((0U, "A"), enumerator.Current);

		Assert.True(enumerator.MoveNext());
		Assert.Equal((1U, null), enumerator.Current);

		Assert.False(enumerator.MoveNext());
	}

	private struct NotNullTableDefinition : IParameterlessStatement {
		public static ReadOnlySpan<byte> CommandText => """
		                                                create table if not exists test_table (
		                                                    col0 UINTEGER not null primary key,
		                                                    col1 VARCHAR not null
		                                                );
		                                                """u8;
	}

	private struct NullableTableDefinition : IParameterlessStatement {
		public static ReadOnlySpan<byte> CommandText => """
		                                                create table if not exists test_table (
		                                                    col0 UINTEGER not null primary key,
		                                                    col1 VARCHAR
		                                                );
		                                                """u8;
	}

	private struct InsertStatement: IPreparedStatement<(uint, string?)> {
		public static BindingContext Bind(ref readonly (uint, string?) args, BindingSource source) => new(source) {
			args.Item1,
			args.Item2
		};

		public static ReadOnlySpan<byte> CommandText => "INSERT INTO test_table VALUES ($1, $2);"u8;
	}

	private struct QueryStatement : IQuery<(uint, string)> {
		public static ReadOnlySpan<byte> CommandText => "SELECT * FROM test_table;"u8;

		public static (uint, string) Parse(ref DataChunk.Row row) => (row.ReadUInt32(), row.ReadString());
	}

	private struct NullableQueryStatement : IQuery<(uint, string?)> {
		public static ReadOnlySpan<byte> CommandText => "SELECT * FROM test_table;"u8;

		public static (uint, string?) Parse(ref DataChunk.Row row) => (row.ReadUInt32(), row.TryReadString());
	}
}
