// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal static class CategorySql {
	/// <summary>
	/// Get index records for a given category where log position is greater the start position
	/// </summary>
	public struct CategoryIndexQueryExcl : IQuery<CategoryIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in CategoryIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.Category,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where category=$1 and log_position>$2 and is_deleted=false order by rowid limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for a given category where the log position is greater or equal the start position
	/// </summary>
	public struct CategoryIndexQueryIncl : IQuery<CategoryIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in CategoryIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.Category,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where category=$1 and log_position>=$2 and is_deleted=false order by rowid limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for a given category where log position is less the start position
	/// </summary>
	public struct CategoryIndexBackQueryExcl : IQuery<CategoryIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in CategoryIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.Category,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where category=$1 and log_position<$2 and is_deleted=false order by rowid desc limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for a given category where the log position is less or equal the start position
	/// </summary>
	public struct CategoryIndexBackQueryIncl : IQuery<CategoryIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in CategoryIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.Category,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where category=$1 and log_position<=$2 and is_deleted=false order by rowid desc limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public record struct CategoryIndexQueryArgs(string Category, long StartPosition, int Count);
}
