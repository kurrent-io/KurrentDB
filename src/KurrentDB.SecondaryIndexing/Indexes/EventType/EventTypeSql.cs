// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal static class EventTypeSql {
	public record struct ReadEventTypeIndexQueryArgs(long EventType, long FromSeq, long ToSeq);

	public record struct EventTypeRecord(long EventTypeSeq, long LogPosition, long EventNumber);

	public struct ReadEventTypeIndexQuery : IQuery<ReadEventTypeIndexQueryArgs, EventTypeRecord> {
		public static BindingContext Bind(in ReadEventTypeIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.EventType,
				args.FromSeq,
				args.ToSeq
			};

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select event_type_seq, log_position, event_number
			from idx_all where event_type=$1 and event_type_seq>=$2 and event_type_seq<=$3
			"""u8;

		public static EventTypeRecord Parse(ref DataChunk.Row row)
			=> new(row.ReadInt32(), row.ReadInt64(), row.ReadInt32());
	}

	public struct GetAllEventTypesQuery : IQuery<ReferenceRecord> {
		public static ReadOnlySpan<byte> CommandText => "select id, name from event_type"u8;

		public static ReferenceRecord Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString());
	}

	public struct GetEventTypeMaxSequencesQuery : IQuery<(long Id, long Sequence)> {
		public static ReadOnlySpan<byte> CommandText =>
			"select event_type, max(event_type_seq) from idx_all group by event_type"u8;

		public static (long Id, long Sequence) Parse(ref DataChunk.Row row) =>
			(row.ReadInt64(), row.ReadInt64());
	}

	public record struct AddEventTypeStatementArgs(long Id, string EventType);

	public struct AddEventTypeStatement : IPreparedStatement<AddEventTypeStatementArgs> {
		public static BindingContext Bind(in AddEventTypeStatementArgs args, PreparedStatement statement)
			=> new(statement) { args.Id, args.EventType };

		public static ReadOnlySpan<byte> CommandText => "insert or ignore into event_type (id, name) values ($1, $2)"u8;
	}
}
