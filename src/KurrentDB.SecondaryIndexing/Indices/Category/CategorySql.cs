// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indices.Category;

internal static class CategorySql {
	public struct QueryCategoryIndexSql : IQuery<(long, long, long), CategoryRecord> {
		public static BindingContext Bind(in (long, long, long) args, PreparedStatement statement) => new(statement) {
			args.Item1,
			args.Item2,
			args.Item3
		};

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select category_seq, log_position, event_number
			from idx_all where category=$1 and category_seq>=$2 and category_seq<=$3
			"""u8;

		public static CategoryRecord Parse(ref DataChunk.Row row) => new() {
			category_seq = row.ReadInt32(),
			log_position = row.ReadInt64(),
			event_number = row.ReadInt32(),
		};
	}

	public struct QueryCategorySql : IQuery<ReferenceRecord> {
		public static ReadOnlySpan<byte> CommandText =>
			"select id, name from category"u8;

		public static ReferenceRecord Parse(ref DataChunk.Row row) => new() {
			id = row.ReadInt32(),
			name = row.ReadString()
		};
	}

	public struct QueryCategoriesMaxSequencesSql : IQuery<(long Id, long Sequence)> {
		public static ReadOnlySpan<byte> CommandText =>
			"SELECT category, max(category_seq) FROM idx_all GROUP BY category"u8;

		public static (long Id, long Sequence) Parse(ref DataChunk.Row row) =>
			(row.ReadInt64(), row.ReadInt64());
	}
}
