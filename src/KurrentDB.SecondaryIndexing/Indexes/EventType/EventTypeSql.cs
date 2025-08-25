// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

static class EventTypeSql {
	public record struct ReadEventTypeIndexQueryArgs(int EventTypeId, long StartPosition, int Count);

	/// <summary>
	/// Get index records for a given event type where the log position is greater or equal the start position
	/// </summary>
	public struct ReadEventTypeIndexQuery : IQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadEventTypeIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.EventTypeId,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText => "select rowid, log_position from idx_all where event_type_id=$1 and log_position>=$2 order by rowid limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for a given event type where the log position is less or equal the start position
	/// </summary>
	public struct ReadEventTypeIndexBackQuery : IQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadEventTypeIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.EventTypeId,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText => "select rowid, log_position from idx_all where event_type_id=$1 and log_position<=$2 order by rowid limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	public struct GetAllEventTypesQuery : IQuery<ReferenceRecord> {
		public static ReadOnlySpan<byte> CommandText => "select id, name from event_types"u8;

		public static ReferenceRecord Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString());
	}

	public record struct AddEventTypeStatementArgs(int Id, string EventType);

	public struct AddEventTypeStatement : IPreparedStatement<AddEventTypeStatementArgs> {
		public static BindingContext Bind(in AddEventTypeStatementArgs args, PreparedStatement statement)
			=> new(statement) { args.Id, args.EventType };

		public static ReadOnlySpan<byte> CommandText => "insert or ignore into event_types (id, name) values ($1, $2)"u8;
	}
}
