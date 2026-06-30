// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Stats;

public record struct CategoryName(string Category);

public static class StatsSql {
	public struct GetAllCategories : IQuery<CategoryName> {
		public static ReadOnlySpan<byte> CommandText => "select distinct category from idx_all_snapshot"u8;

		public static CategoryName Parse(ref DataChunk.Row row) => new(row.ReadString());
	}

	public struct GetCategoryStats : IQuery<GetCategoryStats.Args, GetCategoryStats.Result> {
		public record struct Args(string Category);

		public record struct Result(long StreamCount, long EventCount);

		public static StatementBindingResult Bind(in Args args, PreparedStatement statement) => new(statement) { args.Category };

		public static ReadOnlySpan<byte> CommandText => "select count(distinct stream)::bigint, count(*)::bigint from idx_all_snapshot where category = $1"u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public struct GetCategoryEventTypes : IQuery<GetCategoryEventTypes.Args, GetCategoryEventTypes.Result> {
		public record struct Args(string Category);

		public record struct Result(string EventType, long NumEvents, DateTime FirstAdded, DateTime LastAdded);

		public static StatementBindingResult Bind(in Args args, PreparedStatement statement) => new(statement) { args.Category };

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select
				schema_name,
				count(*)::bigint,
				epoch_ms(min(created_at)),
				epoch_ms(max(created_at))
			from idx_all_snapshot
			where category = $category
			group by schema_name
			"""u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.ReadString(), row.ReadInt64(), row.ReadDateTime(), row.ReadDateTime());
	}

	public struct GetExplicitTransactions : IQuery<GetExplicitTransactions.Result> {
		public record struct Result(string Category, long TransactionCount, DateTime LastTransactionDate);

		public static ReadOnlySpan<byte> CommandText
			=> "select category, count(distinct commit_position)::bigint, max(created_at) from idx_all_snapshot where commit_position not null group by category"u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.ReadString(), row.ReadInt64(), row.ReadDateTime());
	}

	public struct GetLongestStreams : IQuery<GetLongestStreams.Result> {
		public record struct Result(string Stream, long EventNumber);

		public static ReadOnlySpan<byte> CommandText
			=> "SELECT DISTINCT ON(category) stream, stream_revision FROM idx_all_snapshot ORDER BY stream_revision DESC;"u8;

		public static Result Parse(ref DataChunk.Row row) {
			return new(row.ReadString(), row.ReadInt64());
		}
	}
}
