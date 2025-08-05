// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal static class DefaultSql {
	public record struct ReadDefaultIndexQueryArgs(long StartPosition, int Count);

	public struct ReadDefaultIndexQuery : IQuery<ReadDefaultIndexQueryArgs, long> {
		public static BindingContext Bind(in ReadDefaultIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StartPosition, args.Count };

		public static ReadOnlySpan<byte> CommandText =>
			"select log_position from idx_all where log_position>$1 limit $2"u8;

		public static long Parse(ref DataChunk.Row row) => row.ReadInt64();
	}

	public struct GetLastLogPositionSql : IQuery<Optional<long>> {
		public static ReadOnlySpan<byte> CommandText => "select max(log_position) from idx_all"u8;

		public static bool UseStreamingMode => false;

		public static Optional<long> Parse(ref DataChunk.Row row) => row.TryReadInt64().ToOptional();
	}
}
