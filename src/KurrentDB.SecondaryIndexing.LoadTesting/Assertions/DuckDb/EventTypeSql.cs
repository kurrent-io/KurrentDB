// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

public static class EventTypeSql {
	public record struct EventTypeSummary(int Id, string Name, long LastLogPosition);

	public struct GetEventTypesSummaryQuery : IQuery<EventTypeSummary> {
		public static ReadOnlySpan<byte> CommandText =>
			"""
			SELECT e.id, e.name, max_seq.max_event_type_seq
			FROM event_type e
			LEFT JOIN (
			    SELECT event_type, MAX(event_type_seq) AS max_event_type_seq
			    FROM idx_all
			    GROUP BY event_type
			) max_seq ON e.id = max_seq.event_type;
			"""u8;

		public static EventTypeSummary Parse(ref DataChunk.Row row) => new(row.ReadInt32(), row.ReadString(), row.ReadInt64());
	}
}
