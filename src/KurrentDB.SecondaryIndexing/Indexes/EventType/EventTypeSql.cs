// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal static class EventTypeSql {
	public record struct ReadEventTypeIndexQueryArgs(string EventType, long StartPosition, long EndPosition, int Count);

	/// <summary>
	/// Get index records for a given event type where the log position is greater than the start position
	/// </summary>
	public struct ReadEventTypeIndexQueryExcl : IQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadEventTypeIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.EventType,
				args.StartPosition,
				args.EndPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText => "select log_position, event_number from idx_all where event_type=$1 and log_position>$2 and log_position<$3 order by rowid limit $4"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for a given event type where the log position is greater or equal the start position
	/// </summary>
	public struct ReadEventTypeIndexQueryIncl : IQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadEventTypeIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.EventType,
				args.StartPosition,
				args.EndPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText => "select log_position, event_number from idx_all where event_type=$1 and log_position>=$2 and log_position<$3 order by rowid limit $4"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for a given event type where the log position is less than the start position
	/// </summary>
	public struct ReadEventTypeIndexBackQueryExcl : IQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadEventTypeIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.EventType,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText => "select log_position, event_number from idx_all where event_type=$1 and log_position<$2 order by rowid desc limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Get index records for a given event type where the log position is less or equal the start position
	/// </summary>
	public struct ReadEventTypeIndexBackQueryIncl : IQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord> {
		public static BindingContext Bind(in ReadEventTypeIndexQueryArgs args, PreparedStatement statement)
			=> new(statement) {
				args.EventType,
				args.StartPosition,
				args.Count
			};

		public static ReadOnlySpan<byte> CommandText => "select log_position, event_number from idx_all where event_type=$1 and log_position<=$2 order by rowid limit $3"u8;

		public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.ReadInt64());
	}
}
