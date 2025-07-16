// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal static class DefaultSql {
	public struct DefaultIndexQuery : IQuery<(long, long), AllRecord> {
		public static BindingContext Bind(in (long, long) args, PreparedStatement statement)
			=> new(statement) { args.Item1, args.Item2 };

		public static ReadOnlySpan<byte> CommandText =>
			"select seq, log_position from idx_all where seq>=$1 and seq<=$2"u8;

		public static AllRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public struct GetLastSequenceSql : IQuery<Optional<long>> {
		public static ReadOnlySpan<byte> CommandText => "select max(seq) from idx_all"u8;

		public static bool UseStreamingMode => false;

		public static Optional<long> Parse(ref DataChunk.Row row) => row.TryReadInt64().ToOptional();
	}

	public struct GetLastLogPositionSql : IQuery<Optional<long>> {
		public static ReadOnlySpan<byte> CommandText => "select max(log_position) from idx_all"u8;

		public static bool UseStreamingMode => false;

		public static Optional<long> Parse(ref DataChunk.Row row) => row.TryReadInt64().ToOptional();
	}
}

internal record struct AllRecord(long Seq, long LogPosition);
