// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Buffers.Binary;
using Xunit;

namespace Kurrent.Quack;

public sealed class DuckDBAdvancedConnectionTests : DuckDbTests<DuckDBAdvancedConnectionTests> {
	[Fact]
	public void InsertValues() {
		using var connection = new DuckDBAdvancedConnection { ConnectionString = ConnectionString };
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
		using var connection = new DuckDBAdvancedConnection { ConnectionString = ConnectionString };
		connection.Open();

		Assert.Equal(0L, connection.ExecuteNonQuery<NullableTableDefinition>());
		Assert.Equal(1L, connection.ExecuteNonQuery<(uint Col0, string? Col1), InsertStatement>((Col0: 0U, Col1: "A")));
		Assert.Equal(1L, connection.ExecuteNonQuery<(uint, string?), InsertStatement>((1U, null)));

		using var enumerator = connection.ExecuteQuery<(uint, string?), NullableQueryStatement>().GetEnumerator();

		Assert.True(enumerator.MoveNext());
		Assert.Equal((0U, "A"), enumerator.Current);

		Assert.True(enumerator.MoveNext());
		Assert.Equal((1U, null), enumerator.Current);

		Assert.False(enumerator.MoveNext());
	}

	[Fact]
	public void DirectAccessToColumns() {
		using var connection = new DuckDBAdvancedConnection { ConnectionString = ConnectionString };
		connection.Open();

		Assert.Equal(0L, connection.ExecuteNonQuery<NullableTableDefinition>());
		Assert.Equal(1L, connection.ExecuteNonQuery<(uint Col0, string? Col1), InsertStatement>((Col0: 0U, Col1: "A")));
		Assert.Equal(1L, connection.ExecuteNonQuery<(uint, string?), InsertStatement>((1U, null)));

		using var result = connection.ExecuteQuery<NullableQueryStatement>();

		Assert.True(result.TryFetch(out var chunk));
		var col0 = chunk[0];
		Assert.False(col0.IsNullable);
		Assert.Equal([0U, 1U], col0.UInt32Rows);

		var col1 = chunk[1];
		Assert.True(col1.IsNullable);
		Assert.True(col1[0]);
		Assert.Equal("A"u8, col1.BlobRows[0].AsSpan());
		Assert.Equal("A", col1.BlobRows[0].ToUtf16String());

		Assert.False(col1[1]);

		Assert.False(result.TryFetch(out chunk));
	}

	[Fact]
	public void SerializeDeserializeBlittableTypes() {
		using var connection = new DuckDBAdvancedConnection { ConnectionString = ConnectionString };
		connection.Open();

		using (var command = connection.CreateCommand()) {
			command.CommandText = """
			                      create table if not exists test_table (
			                          col0 UINTEGER not null primary key,
			                          col1 BLOB not null
			                      );
			                      """;

			command.ExecuteNonQuery();
		}

		var guid0 = Guid.NewGuid();
		connection.ExecuteNonQuery<(uint, Guid), InsertGuidStatement>((0U, guid0));

		var guid1 = Guid.NewGuid();
		connection.ExecuteNonQuery<(uint, Guid), InsertGuidStatement>((1U, guid1));

		using var result = connection.ExecuteQuery<(uint, Guid), QueryWithGuidStatement>().GetEnumerator();

		Assert.True(result.MoveNext());
		Assert.Equal((0U, guid0), result.Current);

		Assert.True(result.MoveNext());
		Assert.Equal((1U, guid1), result.Current);

		Assert.False(result.MoveNext());
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
		public static BindingContext Bind(in (uint, string?) args, PreparedStatement statement) => new(statement) {
			args.Item1,
			args.Item2,
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

	private struct QueryWithGuidStatement : IQuery<(uint, Guid)> {
		public static ReadOnlySpan<byte> CommandText => "SELECT * FROM test_table;"u8;

		public static (uint, Guid) Parse(ref DataChunk.Row row) =>
			(row.ReadUInt32(), row.ReadBlob<Blittable<Guid>>().Value);
	}

	private struct InsertGuidStatement : IPreparedStatement<(uint, Guid)> {
		public static BindingContext Bind(in (uint, Guid) args, PreparedStatement statement) => new(statement) {
			args.Item1,
			new Blittable<Guid> { Value = args.Item2 }
		};

		public static ReadOnlySpan<byte> CommandText => "INSERT INTO test_table VALUES ($1, $2);"u8;
	}
}
