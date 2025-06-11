// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using Dapper;
using EventStore.Core.Data;
using EventStore.Core.Metrics;

namespace EventStore.Core.Duck.Default;

public class EventTypeIndex(DuckDbDataSource db) {
	Dictionary<long, string> _eventTypeIds = new();
	public Dictionary<string, long> EventTypes = new();
	readonly Dictionary<long, long> _sequences = new();

	public void Init() {
		using var connection = db.OpenConnection();
		var ids = connection.Query<ReferenceRecord>("select * from event_type").ToList();

		_eventTypeIds = ids.ToDictionary(x => x.id, x => x.name);
		EventTypes = ids.ToDictionary(x => x.name, x => x.id);
		Seq = _eventTypeIds.Count > 0 ? _eventTypeIds.Keys.Max() : 0;

		foreach (var id in ids) {
			_sequences[id.id] = -1;
		}

		const string query = "select event_type, max(event_type_seq) from idx_all group by event_type";

		var sequences = connection.Query<(long Id, long Sequence)>(query);
		foreach (var sequence in sequences) {
			_sequences[sequence.Id] = sequence.Sequence;
		}
	}

	public long GetLastEventNumber(long id) => _sequences.TryGetValue(id, out var size) ? size : ExpectedVersion.NoStream;

	public IEnumerable<IndexedPrepare> GetRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = QueryEventType(id, fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.event_type_seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	List<EventTypeRecord> QueryEventType(long eventTypeId, long fromEventNumber, long toEventNumber) {
		const string query = """
		                     select event_type_seq, log_position, event_number
		                     from idx_all where event_type=$et and event_type_seq>=$start and event_type_seq<=$end
		                     """;

		using var duration = TempIndexMetrics.MeasureIndex("duck_get_et_range");
		using var connection = db.Pool.Open();

		return connection.Query<EventTypeRecord>(query, new { et = eventTypeId, start = fromEventNumber, end = toEventNumber }).ToList();
	}

	public SequenceRecord Handle(EventRecord eventRecord) {
		LastPosition = eventRecord.LogPosition;
		if (EventTypes.TryGetValue(eventRecord.EventType, out var val)) {
			var next = _sequences[val] + 1;
			_sequences[val] = next;
			return new(val, next);
		}

		var id = ++Seq;

		using var connection = db.Pool.Open();
		connection.Execute(Sql, new { id, name = eventRecord.EventType });

		EventTypes[eventRecord.EventType] = id;
		_eventTypeIds[id] = eventRecord.EventType;
		_sequences[id] = 0;
		return new(id, 0);
	}

	internal long LastPosition { get; private set; }

	static long Seq;
	static readonly string Sql = Default.Sql.AppendIndexSql.Replace("{table}", "event_type");
}
