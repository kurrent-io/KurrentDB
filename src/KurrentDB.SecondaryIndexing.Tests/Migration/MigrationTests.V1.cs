// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Tests.Migration;

partial class MigrationTests {
	[Fact]
	public void UpgradeToV1() {
		// Setup
		UpgradeTo(desiredVersion: 1);

		// Check new schema
		CheckIdxAllTable(_connection);
		CheckUserIndexTable(_connection);

		static void CheckIdxAllTable(DuckDBAdvancedConnection connection) {
			var columns = connection
				.ExecuteQuery<ValueTuple<string>, string, ColumnNamesQuery>(new("idx_all"))
				.ToHashSet();

			Assert.Contains("record_id", columns);
			Assert.Contains("created_at", columns);
			Assert.Contains("expires_at", columns);
			Assert.Contains("deleted", columns);
			Assert.Contains("schema_name", columns);
			Assert.Contains("stream_revision", columns);

			Assert.DoesNotContain("event_type", columns);
			Assert.DoesNotContain("created", columns);
			Assert.DoesNotContain("event_number", columns);
			Assert.DoesNotContain("is_deleted", columns);
			Assert.DoesNotContain("expires", columns);
		}

		static void CheckUserIndexTable(DuckDBAdvancedConnection connection) {
			var columns = connection
				.ExecuteQuery<ValueTuple<string>, string, ColumnNamesQuery>(new("idx_user__myindex"))
				.ToHashSet();

			Assert.Contains("record_id", columns);
			Assert.Contains("created_at", columns);
			Assert.Contains("stream_revision", columns);

			Assert.DoesNotContain("created", columns);
			Assert.DoesNotContain("event_number", columns);
		}
	}

	[Fact]
	public void UpgradeToV1BackfillsRecordIdWithEmptyBlob() {
		// record_id is added with a bare DEFAULT '' so DuckDB takes the cheap metadata path rather
		// than rewriting every row - see the comment on IndexingDbSchema.UpgradeToV1B. '' is a VARCHAR
		// literal that DuckDB implicitly casts to BLOB, so pin down what that cast produces for the
		// existing rows: a zero-length BLOB. Not NULL, and not the single 0x00 byte that a
		// NUL-terminated string representation would yield. If a future DuckDB ever changes the cast,
		// this fails instead of silently writing a one-byte value into every indexed record.
		const int rows = 10;
		SeedV0Rows(_connection, rows);

		UpgradeTo(desiredVersion: 1);

		var stats = _connection.ExecuteQuery<RecordIdStats, RecordIdStatsQuery>().ToList();
		string[] tables = ["idx_all", "idx_user__myindex"];

		foreach (var table in tables) {
			var actual = stats.Single(x => x.Table == table);

			Assert.Equal(rows, actual.Rows);
			Assert.Equal(0, actual.Nulls);
			Assert.Equal(0, actual.NonEmptyBytes);
			Assert.Equal(0, actual.SingleNulByte);
		}
	}

	private readonly record struct RecordIdStats(string Table, long Rows, long Nulls, long NonEmptyBytes, long SingleNulByte);

	private readonly struct RecordIdStatsQuery : IQuery<RecordIdStats> {
		// Backslashes are literal in a raw string literal, so DuckDB receives '\x00' and parses it as
		// a one byte blob.
		public static ReadOnlySpan<byte> CommandText => """
			select 'idx_all',
					count(*),
					count(*) filter (where record_id is null),
					count(*) filter (where octet_length(record_id) <> 0),
					count(*) filter (where record_id = '\x00'::blob)
			from idx_all
			union all
			select 'idx_user__myindex',
					count(*),
					count(*) filter (where record_id is null),
					count(*) filter (where octet_length(record_id) <> 0),
					count(*) filter (where record_id = '\x00'::blob)
			from idx_user__myindex;
			"""u8;

		public static RecordIdStats Parse(ref DataChunk.Row row) =>
			new(row.ReadString(), row.ReadInt64(), row.ReadInt64(), row.ReadInt64(), row.ReadInt64());
	}
}
