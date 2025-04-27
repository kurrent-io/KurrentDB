using DotNext.Buffers.Binary;
using DuckDB.NET.Data;
using FluentStorage.Utils.Extensions;
using Xunit;

namespace KurrentDB.Duck.Tests;

public sealed class AppenderTests : DuckDbTests<AppenderTests> {
	[Fact]
	public void AppendDataTypes() {
		using var connection = new DuckDBConnection(ConnectionString);
		connection.Open();

		// create test table
		const string tableDefinition = """
		                               create table if not exists test_table (
		                                   col0 UINTEGER primary key,
		                                   col1 VARCHAR,
		                                   col2 BINARY,
		                                   col3 LONG,
		                                   col4 DATETIME
		                               );
		                               """;

		var command = connection.CreateCommand();
		command.CommandText = tableDefinition;
		command.ExecuteNonQuery();

		var dt = DateTime.UtcNow;
		var guid = Guid.NewGuid();
		using (var appender = new Appender(connection, "test_table"u8)) {
			using (var row = appender.CreateRow()) {
				row.Append((uint)42);
				row.Append("Row 0");
				row.Append([10, 20]);
				row.Append(42L);
				row.Append(dt);
			}

			using (var row = appender.CreateRow()) {
				row.Append((uint)43);
				row.Append("Row 1"u8);
				row.Append(new Blittable<Guid> { Value = guid });
				row.Append(43L);
				row.Append(dt);
			}

			appender.Flush();
		}

		command.CommandText = "SELECT * FROM test_table;";
		using (var reader = command.ExecuteReader()) {
			// row 0
			Assert.True(reader.Read());
			Assert.Equal(42U, reader.GetValue(0));
			Assert.Equal("Row 0", reader.GetValue(1));

			using (var unmanagedStream = (UnmanagedMemoryStream)reader.GetValue(2)) {
				Assert.Equal<byte>([10, 20], unmanagedStream.ToByteArray().AsSpan());
			}

			Assert.Equal(42L, reader.GetValue(3));
			Assert.Equal(dt.Date, ((DateTime)reader.GetValue(4)).Date);

			// row 1
			Assert.True(reader.Read());
			Assert.Equal(43U, reader.GetValue(0));
			Assert.Equal("Row 1", reader.GetValue(1));

			using (var unmanagedStream = (UnmanagedMemoryStream)reader.GetValue(2)) {
				Assert.Equal(guid.ToByteArray(), unmanagedStream.ToByteArray());
			}

			Assert.Equal(43L, reader.GetValue(3));
			Assert.Equal(dt.Date, ((DateTime)reader.GetValue(4)).Date);
		}
	}
}
