// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Stats;

public record struct CategoryName(string Category);

public static class StatsSql {
	public struct GetAllCategories : IQuery<CategoryName> {
		public static ReadOnlySpan<byte> CommandText => "select distinct category from idx_all"u8;

		public static CategoryName Parse(ref DataChunk.Row row) => new(row.ReadString());
	}

	public struct GetTotalStats : IQuery<GetTotalStats.Result> {
		public record struct Args(string Category);

		public record struct Result(long StreamCount, long EventCount);

		public static StatementBindingResult Bind(in Args args, PreparedStatement statement) => new(statement) { args.Category };

		public static ReadOnlySpan<byte> CommandText => "select count(distinct stream)::bigint, count(rowid)::bigint from idx_all"u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public struct GetCategoryStats : IQuery<GetCategoryStats.Args, GetCategoryStats.Result> {
		public record struct Args(string Category);

		public record struct Result(long StreamCount, long EventCount);

		public static StatementBindingResult Bind(in Args args, PreparedStatement statement) => new(statement) { args.Category };

		public static ReadOnlySpan<byte> CommandText => "select count(distinct stream)::bigint, count(rowid)::bigint from idx_all where category = $1"u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public struct GetCategoryEventTypes : IQuery<GetCategoryEventTypes.Args, GetCategoryEventTypes.Result> {
		public record struct Args(string Category);

		public record struct Result(string EventType, long NumEvents, DateTime FirstAdded, DateTime LastAdded);

		public static StatementBindingResult Bind(in Args args, PreparedStatement statement) => new(statement) { args.Category };

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select
				event_type,
				count(rowid)::bigint,
				epoch_ms(min(created)),
				epoch_ms(max(created))
			from idx_all
			where category = $category
			group by event_type
			"""u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.ReadString(), row.ReadInt64(), row.ReadDateTime(), row.ReadDateTime());
	}

	public struct GetExplicitTransactions : IQuery<GetExplicitTransactions.Result> {
		public record struct Result(string Category, long TransactionCount, DateTime LastTransactionDate);

		public static ReadOnlySpan<byte> CommandText
			=> "select category, count(distinct commit_position)::bigint, max(created) from idx_all where commit_position not null group by category"u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.ReadString(), row.ReadInt64(), row.ReadDateTime());
	}

	public struct GetLongestStreams : IQuery<GetLongestStreams.Result> {
		public record struct Result(string Stream, long EventNumber);

		public static ReadOnlySpan<byte> CommandText
			=> "SELECT DISTINCT ON(category) stream, event_number FROM idx_all ORDER BY event_number DESC;"u8;

		public static Result Parse(ref DataChunk.Row row) {
			return new(row.ReadString(), row.ReadInt64());
		}
	}
}
