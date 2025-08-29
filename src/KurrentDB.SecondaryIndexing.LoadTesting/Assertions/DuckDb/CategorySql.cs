// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

public static class CategorySql {
	public record struct CategorySummary(int Id, string Name, long LastLogPosition);

	public struct GetCategoriesSummaryQuery : IQuery<CategorySummary> {
		public static ReadOnlySpan<byte> CommandText =>
			"""
			SELECT c.id, c.name, max_log_pos.max_category_log_pos
			FROM category c
			LEFT JOIN (
			    SELECT category_id, MAX(log_position) AS max_category_log_pos
			    FROM idx_all
			    GROUP BY category_id
			) max_log_pos ON c.id = max_log_pos.category_id;
			"""u8;

		public static CategorySummary Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString(), row.ReadInt64());
	}
}
