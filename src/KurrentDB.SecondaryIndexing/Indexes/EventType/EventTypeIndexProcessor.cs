// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Readers;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndexProcessor {
	private readonly Dictionary<string, int> _eventTypes;
	private readonly Dictionary<int, long> _eventTypeSizes = new();
	private readonly DuckDbDataSource _db;
	private readonly IPublisher _publisher;

	private int _seq;
	public long LastIndexedPosition { get; private set; }

	public EventTypeIndexProcessor(DuckDbDataSource db, IPublisher publisher) {
		_db = db;
		_publisher = publisher;

		var ids = db.Pool.Query<ReferenceRecord, GetAllEventTypesQuery>();
		_eventTypes = ids.ToDictionary(x => x.Name, x => x.Id);

		foreach (var id in ids) {
			_eventTypeSizes[id.Id] = -1;
		}

		var sequences = db.Pool.Query<(int Id, long Sequence), GetEventTypeMaxSequencesQuery>();
		foreach (var sequence in sequences) {
			_eventTypeSizes[sequence.Id] = sequence.Sequence;
		}

		_seq = _eventTypes.Count > 0 ? _eventTypes.Values.Max() - 1 : -1;
	}

	public SequenceRecord Index(ResolvedEvent resolvedEvent) {
		var eventTypeName = resolvedEvent.OriginalEvent.EventType;

		if (_eventTypes.TryGetValue(eventTypeName, out var eventTypeId)) {
			var next = _eventTypeSizes[eventTypeId] + 1;
			_eventTypeSizes[eventTypeId] = next;
			LastIndexedPosition = resolvedEvent.Event.LogPosition;

			_publisher.Publish(
				new StorageMessage.SecondaryIndexCommitted(
					resolvedEvent.ToResolvedLink($"{EventTypeIndex.IndexPrefix}{eventTypeName}", next))
			);

			return new(eventTypeId, next);
		}

		var id = ++_seq;

		_eventTypes[eventTypeName] = id;
		_eventTypeSizes[id] = 0;

		_db.Pool.ExecuteNonQuery<AddEventTypeStatementArgs, AddEventTypeStatement>(new(id, eventTypeName));
		LastIndexedPosition = resolvedEvent.Event.LogPosition;

		_publisher.Publish(
			new StorageMessage.SecondaryIndexCommitted(
				resolvedEvent.ToResolvedLink($"{EventTypeIndex.IndexPrefix}{eventTypeName}", 0))
		);

		return new(id, 0);
	}

	public long GetLastEventNumber(int eventTypeId) =>
		_eventTypeSizes.TryGetValue(eventTypeId, out var size) ? size : ExpectedVersion.NoStream;

	public int GetEventTypeId(string eventTypeName) =>
		_eventTypes.TryGetValue(eventTypeName, out var eventTypeId) ? eventTypeId : -1;
}
