// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

public static class CategorySql {
	public record struct CategorySummary(int Id, string Name, long LastLogPosition);

	public struct GetCategoriesSummaryQuery : IQuery<CategorySummary> {
		public static ReadOnlySpan<byte> CommandText =>
			"""
			SELECT c.id, c.name, max_seq.max_category_seq
			FROM category c
			LEFT JOIN (
			    SELECT category, MAX(category_seq) AS max_category_seq
			    FROM idx_all
			    GROUP BY category
			) max_seq ON c.id = max_seq.category;
			"""u8;

		public static CategorySummary Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString(), row.ReadInt64());
	}
}
