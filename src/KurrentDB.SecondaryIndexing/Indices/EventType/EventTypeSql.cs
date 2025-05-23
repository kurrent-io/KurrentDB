// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indices.EventType;

internal static class EventTypeSql {
	public struct QueryEventTypeIndexSql : IQuery<(long, long, long), EventTypeRecord> {
		public static BindingContext Bind(in (long, long, long) args, PreparedStatement statement) => new(statement) {
			args.Item1,
			args.Item2,
			args.Item3
		};

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select event_type_seq, log_position, event_number
			from idx_all where event_type=$1 and event_type_seq>=$2 and event_type_seq<=$3
			"""u8;

		public static EventTypeRecord Parse(ref DataChunk.Row row) => new() {
			event_type_seq = row.ReadInt32(),
			log_position = row.ReadInt64(),
			event_number = row.ReadInt32(),
		};
	}

	public struct QueryEventTypeSql : IQuery<ReferenceRecord> {
		public static ReadOnlySpan<byte> CommandText =>
			"select id, name from event_type"u8;

		public static ReferenceRecord Parse(ref DataChunk.Row row) => new() {
			id = row.ReadInt32(),
			name = row.ReadString()
		};
	}

	public struct QueryCategoriesMaxSequencesSql : IQuery<(long Id, long Sequence)> {
		public static ReadOnlySpan<byte> CommandText =>
			"SELECT event_type, max(event_type_seq) FROM idx_all GROUP BY event_type"u8;

		public static (long Id, long Sequence) Parse(ref DataChunk.Row row) =>
			(row.ReadInt64(), row.ReadInt64());
	}
}
