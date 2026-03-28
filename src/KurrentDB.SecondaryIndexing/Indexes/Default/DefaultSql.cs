// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal static class DefaultSql {
	// We should query on this view rather than on idx_all table.
	// The view combines in-flight cache and idx_all rows.
	public const string DefaultIndexViewName = "idx_all_snapshot";

	public record struct ReadDefaultIndexQueryArgs(long StartPosition, int Count);

	/// <summary>
	/// Get index records for the default index with a log position greater than the start position
	/// </summary>
	public struct ReadDefaultIndexQueryExcl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static StatementBindingResult Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		// We cannot sort by 'rowid' because this hidden column exists only for rows in the physical table.
		// Moreover, DuckDB cannot incrementally return query result by pages (data chunks), because ordering must be applied
		// first on all rows, which leads to full materialization of the query result in the memory.
		public static ReadOnlySpan<byte> CommandText =>
			"select log_position, commit_position, event_number from idx_all_snapshot where log_position>$1 and is_deleted=false order by coalesce(commit_position, log_position) limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for the default index with log position greater or equal than the start position
	/// </summary>
	public struct ReadDefaultIndexQueryIncl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static StatementBindingResult Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select log_position, commit_position, event_number from idx_all_snapshot where log_position>=$1 and is_deleted=false order by coalesce(commit_position, log_position) limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for the default index with the log position less than the start position
	/// </summary>
	public struct ReadDefaultIndexBackQueryExcl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static StatementBindingResult Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select log_position, commit_position, event_number from idx_all_snapshot where log_position<$1 and is_deleted=false order by coalesce(commit_position, log_position) desc, log_position desc limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for the default index with log position less or equal than the start position
	/// </summary>
	public struct ReadDefaultIndexBackQueryIncl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static StatementBindingResult Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select log_position, commit_position, event_number from idx_all_snapshot where log_position<=$1 and is_deleted=false order by coalesce(commit_position, log_position) desc, log_position desc limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
	}

	public record struct LastPositionResult(long PreparePosition, long? CommitPosition, long Timestamp);

	/// <summary>
	/// Get the last indexed log position
	/// </summary>
	public struct GetLastLogPositionQuery : IQuery<LastPositionResult> {
		public static ReadOnlySpan<byte> CommandText => "select log_position, commit_position, created from idx_all order by rowid desc limit 1"u8;

		public static LastPositionResult Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
	}
}
