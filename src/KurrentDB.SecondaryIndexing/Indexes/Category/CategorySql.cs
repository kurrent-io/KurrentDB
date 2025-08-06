// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

static class CategorySql {
	public struct CategoryIndexQuery : IQuery<CategoryIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in CategoryIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.Id,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select rowid, log_position
			from idx_all where category_id=$1 and log_position>$2 and is_deleted=false limit $3
			"""u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadUInt32(), row.ReadInt64());
	}

	public record struct CategoryIndexQueryArgs(int Id, long StartPosition, int Count);

	public struct GetCategoriesQuery : IQuery<ReferenceRecord> {
		public static ReadOnlySpan<byte> CommandText => "select id, name from category"u8;

		public static ReferenceRecord Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString());
	}

	public record struct AddCategoryStatementArgs(int Id, string Category);

	public struct AddCategoryStatement : IPreparedStatement<AddCategoryStatementArgs> {
		public static BindingContext Bind(in AddCategoryStatementArgs args, PreparedStatement statement)
			=> new(statement) { args.Id, args.Category };

		public static ReadOnlySpan<byte> CommandText => "insert or ignore into category (id, name) values ($1, $2)"u8;
	}
}
