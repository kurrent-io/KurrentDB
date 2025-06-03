// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal static class StreamSql {
	public record struct GetStreamIdByNameQueryArgs(string StreamName);

	public struct GetStreamIdByNameQuery : IQuery<GetStreamIdByNameQueryArgs, long> {
		public static BindingContext Bind(in GetStreamIdByNameQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StreamName };

		public static ReadOnlySpan<byte> CommandText => "select id from streams where name=$1 limit 1"u8;

		public static long Parse(ref DataChunk.Row row) => row.ReadInt64();
	}

	public struct GetStreamMaxSequencesQuery : IQuery<long> {
		public static ReadOnlySpan<byte> CommandText => "select max(id) from streams"u8;

		public static long Parse(ref DataChunk.Row row) => row.ReadInt64();
	}
}
