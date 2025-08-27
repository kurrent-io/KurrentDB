// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

public class EventTypeIndexProcessor {
	readonly Dictionary<string, int> _eventTypes;
	readonly DuckDbDataSource _db;
	readonly IPublisher _publisher;

	int _seq;
	public TFPos LastIndexedPosition { get; private set; } = TFPos.HeadOfTf;

	public EventTypeIndexProcessor(DuckDbDataSource db, IPublisher publisher) {
		_db = db;
		_publisher = publisher;

		var ids = db.Pool.Query<ReferenceRecord, GetAllEventTypesQuery>();

		_eventTypes = ids.ToDictionary(x => x.Name, x => x.Id);
		_seq = _eventTypes.Count > 0 ? _eventTypes.Values.Max() : -1;
	}

	public int Index(ResolvedEvent resolvedEvent) {
		var eventTypeName = resolvedEvent.OriginalEvent.EventType;

		if (!_eventTypes.TryGetValue(eventTypeName, out var eventTypeId)) {
			eventTypeId = ++_seq;
			_eventTypes[eventTypeName] = eventTypeId;
			_db.Pool.ExecuteNonQuery<AddEventTypeStatementArgs, AddEventTypeStatement>(new(eventTypeId, eventTypeName));
		}

		LastIndexedPosition = resolvedEvent.OriginalPosition!.Value;

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(EventTypeIndex.Name(eventTypeName), resolvedEvent));

		return eventTypeId;
	}

	public bool TryGetEventTypeId(string eventTypeName, out int eventTypeId) {
		eventTypeId = -1;
		return _eventTypes.TryGetValue(eventTypeName, out eventTypeId);
	}
}
