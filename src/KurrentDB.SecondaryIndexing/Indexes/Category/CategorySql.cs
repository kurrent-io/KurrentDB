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
			select category_seq, log_position, event_number
			from idx_all where category=$1 and category_seq>=$2 and category_seq<=$3
			"""u8;

		public static CategoryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64(), row.ReadInt64());
	}

	public record struct CategoryIndexQueryArgs(long Id, long FromSeq, long ToSeq);

	public record struct CategoryRecord(long CategorySeq, long LogPosition, long EventNumber);

	public struct GetCategoriesQuery : IQuery<ReferenceRecord> {
		public static ReadOnlySpan<byte> CommandText => "select id, name from category"u8;

		public static ReferenceRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadString());
	}

	public struct GetCategoriesMaxSequencesQuery : IQuery<(long Id, long Sequence)> {
		public static ReadOnlySpan<byte> CommandText =>
			"select category, max(category_seq) from idx_all group by category"u8;

		public static (long Id, long Sequence) Parse(ref DataChunk.Row row) => (row.ReadInt64(), row.ReadInt64());
	}

	public record struct AddCategoryStatementArgs(long Id, string Category);

	public struct AddCategoryStatement  : IPreparedStatement<AddCategoryStatementArgs> {
		public static BindingContext Bind(in AddCategoryStatementArgs args, PreparedStatement statement)
			=> new(statement) { args.Id, args.Category };

		public static ReadOnlySpan<byte> CommandText => "insert or ignore into category (id, name) values ($1, $2)"u8;
	}
}
