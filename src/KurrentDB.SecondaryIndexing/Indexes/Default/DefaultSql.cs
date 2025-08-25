// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

static class DefaultSql {
	public record struct ReadDefaultIndexQueryArgs(long StartPosition, int Count);

	/// <summary>
	/// Get index records for the default index with log position greater than the start position
	/// </summary>
	public struct ReadDefaultIndexQueryExcl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where log_position>$1 and is_deleted=false order by rowid limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for the default index with log position greater or equal than the start position
	/// </summary>
	public struct ReadDefaultIndexQueryIncl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where log_position>=$1 and is_deleted=false order by rowid limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for the default index with log position less than the start position
	/// </summary>
	public struct ReadDefaultIndexBackQueryExcl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where log_position<$1 and is_deleted=false order by rowid desc limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for the default index with log position less or equal than the start position
	/// </summary>
	public struct ReadDefaultIndexBackQueryIncl : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select rowid, log_position from idx_all where log_position<=$1 and is_deleted=false order by rowid desc limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public record struct LastPositionResult(long PreparePosition, long? CommitPosition);

	/// <summary>
	/// Get the last indexed log position
	/// </summary>
	public struct GetLastLogPositionQuery : IQuery<LastPositionResult> {
		public static ReadOnlySpan<byte> CommandText => "select log_position, commit_position from idx_all order by rowid desc limit 1"u8;

		public static bool UseStreamingMode => false;

		public static LastPositionResult Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64());
	}

	public record struct GetPrevNextPositionQueryArgs(long FirstPosition, long LastPosition);

	public record struct PositionQueryRecord(long RowId, long LogPosition, long? CommitPosition);

	public struct GetPrevNextPositionQuery : IQuery<GetPrevNextPositionQueryArgs, PositionQueryRecord> {
		public static BindingContext Bind(in GetPrevNextPositionQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.FirstPosition, args.LastPosition };

		public static ReadOnlySpan<byte> CommandText => "select rowid, log_position, commit_position from idx_all where rowid=$1 or rowid=$2"u8;

		public static bool UseStreamingMode => false;

		public static PositionQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64(), row.TryReadInt64());
	}
}
