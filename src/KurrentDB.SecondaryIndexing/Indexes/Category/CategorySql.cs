// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal static class CategorySql {
	public struct CategoryIndexQuery : IQuery<CategoryIndexQueryArgs, CategoryRecord> {
		public static BindingContext Bind(in CategoryIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.Id,
				args.FromSeq,
				args.ToSeq
			};

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select category_seq, log_position
			from idx_all where category=$1 and category_seq>=$2 and category_seq<=$3
			"""u8;

		public static CategoryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public record struct CategoryIndexQueryArgs(int Id, long FromSeq, long ToSeq);

	public record struct CategoryRecord(long CategorySeq, long LogPosition);

	public struct GetCategoriesQuery : IQuery<ReferenceRecord> {
		public static ReadOnlySpan<byte> CommandText => "select id, name from category"u8;

		public static ReferenceRecord Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString());
	}

	public record struct CategorySummary(long Id, string Name, long LastLogPosition);

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

	public struct GetCategoriesMaxSequencesQuery : IQuery<(int Id, long Sequence)> {
		public static ReadOnlySpan<byte> CommandText =>
			"select category, max(category_seq) from idx_all group by category"u8;

		public static (int Id, long Sequence) Parse(ref DataChunk.Row row) => (row.ReadInt32(), row.ReadInt64());
	}

	public record struct AddCategoryStatementArgs(int Id, string Category);

	public struct AddCategoryStatement : IPreparedStatement<AddCategoryStatementArgs> {
		public static BindingContext Bind(in AddCategoryStatementArgs args, PreparedStatement statement)
			=> new(statement) { args.Id, args.Category };

		public static ReadOnlySpan<byte> CommandText => "insert or ignore into category (id, name) values ($1, $2)"u8;
	}
}
