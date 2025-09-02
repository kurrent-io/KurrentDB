// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

public static class EventTypeSql {
	public record struct EventTypeSummary(int Id, string Name, long LastLogPosition);

	public struct GetEventTypesSummaryQuery : IQuery<EventTypeSummary> {
		public static ReadOnlySpan<byte> CommandText =>
			"""
			SELECT e.id, e.name, max_log_pos.max_event_type_log_pos
			FROM event_type e
			LEFT JOIN (
			    SELECT event_type_id, MAX(log_position) AS max_event_type_log_pos
			    FROM idx_all
			    GROUP BY event_type_id
			) max_log_pos ON e.id = max_log_pos.event_type_id;
			"""u8;

		public static EventTypeSummary Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString(), row.ReadInt64());
	}
}
