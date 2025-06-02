// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndexProcessor : ISecondaryIndexProcessor {
	readonly Dictionary<string, long> _eventTypes;
	readonly Dictionary<long, long> _eventTypeSizes = new();
	readonly DuckDbDataSource _db;

	long _lastLogPosition;

	public long Seq { get; private set; }
	public long LastCommittedPosition { get; private set; }

	public EventTypeIndexProcessor(DuckDbDataSource db) {
		_db = db;
		var ids = db.Pool.Query<ReferenceRecord, GetAllEventTypesQuery>();
		_eventTypes = ids.ToDictionary(x => x.Name, x => x.Id);

		foreach (var id in ids) {
			_eventTypeSizes[id.Id] = -1;
		}

		var sequences = db.Pool.Query<(int Id, long Sequence), GetEventTypeMaxSequencesQuery>();
		foreach (var sequence in sequences) {
			_eventTypeSizes[sequence.Id] = sequence.Sequence;
		}

		Seq = _eventTypes.Count > 0 ? _eventTypes.Values.Max() : 0;
	}

	public SequenceRecord Index(ResolvedEvent resolvedEvent) {
		var eventTypeName = resolvedEvent.OriginalEvent.EventType;
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		if (_eventTypes.TryGetValue(eventTypeName, out var eventTypeId)) {
			var next = _eventTypeSizes[eventTypeId] + 1;
			_eventTypeSizes[eventTypeId] = next;

			return new(eventTypeId, next);
		}

		var id = ++Seq;

		_eventTypes[eventTypeName] = id;
		_eventTypeSizes[id] = 0;

		_db.Pool.ExecuteNonQuery<AddEventTypeStatementArgs, AddEventTypeStatement>(new((int)id, eventTypeName));
		_lastLogPosition = resolvedEvent.Event.LogPosition;
		return new(id, 0);
	}

	public long GetLastEventNumber(long eventTypeId) =>
		_eventTypeSizes.TryGetValue(eventTypeId, out var size) ? size : ExpectedVersion.NoStream;

	public long GetEventTypeId(string eventTypeName) =>
		_eventTypes.TryGetValue(eventTypeName, out var eventTypeId) ? eventTypeId : ExpectedVersion.NoStream;

	public void Commit() {
		LastCommittedPosition = _lastLogPosition;
	}
}
