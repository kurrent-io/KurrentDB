// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

static class DefaultSql {
	public record struct ReadDefaultIndexQueryArgs(long StartPosition, int Count);

	public struct ReadDefaultIndexQuery : IQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select log_position from idx_all where log_position>$1 and is_deleted=false limit $2"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadUInt32(), row.ReadInt64());
	}

	public record struct LastPositionResult(long PreparePosition, long? CommitPosition);

	public struct GetLastLogPositionQuery : IQuery<LastPositionResult> {
		public static ReadOnlySpan<byte> CommandText => "select log_position, commit_position from idx_all order by rowid desc limit 1"u8;

		public static bool UseStreamingMode => false;

		public static LastPositionResult Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64());
	}
}
