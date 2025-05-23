// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indices.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indices.EventType;

internal class EventTypeIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private readonly Dictionary<string, long> _eventTypes;
	private readonly Dictionary<long, long> _eventTypeSizes = new();
	private long _lastLogPosition;
	private readonly Appender _appender;

	public long Seq { get; private set; }
	public long LastCommittedPosition { get; private set; }
	public SequenceRecord LastIndexed { get; private set; }

	public EventTypeIndexProcessor(DuckDBAdvancedConnection connection) {
		_appender = new Appender(connection, "event_type"u8);

		var ids = connection.Query<ReferenceRecord, QueryEventTypeSql>();
		_eventTypes = ids.ToDictionary(x => x.name, x => x.id);

		foreach (var id in ids) {
			_eventTypeSizes[id.id] = -1;
		}

		var sequences = connection.Query<(long Id, long Sequence), QueryCategoriesMaxSequencesSql>();
		foreach (var sequence in sequences) {
			_eventTypeSizes[sequence.Id] = sequence.Sequence;
		}

		Seq = _eventTypes.Count > 0 ? _eventTypes.Values.Max() : 0;
	}

	public ValueTask Index(ResolvedEvent resolvedEvent, CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return ValueTask.CompletedTask;

		var eventTypeName = resolvedEvent.OriginalEvent.EventType;
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		if (_eventTypes.TryGetValue(eventTypeName, out var eventTypeId)) {
			var next = _eventTypeSizes[eventTypeId] + 1;
			_eventTypeSizes[eventTypeId] = next;

			LastIndexed = new SequenceRecord(eventTypeId, next);
			return ValueTask.CompletedTask;
		}

		var id = ++Seq;

		_eventTypes[eventTypeName] = id;
		_eventTypeSizes[id] = 0;

		using (var row = _appender.CreateRow()) {
			row.Append(id);
			row.Append(eventTypeName);
		}

		_lastLogPosition = resolvedEvent.Event.LogPosition;
		LastIndexed = new SequenceRecord(id, 0);

		return ValueTask.CompletedTask;
	}

	public long GetLastEventNumber(long eventTypeId) =>
		_eventTypeSizes.TryGetValue(eventTypeId, out var size) ? size : ExpectedVersion.NoStream;

	public long GetEventTypeId(string eventTypeName) =>
		_eventTypes.TryGetValue(eventTypeName, out var eventTypeId) ? eventTypeId : ExpectedVersion.NoStream;

	public ValueTask Commit(CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return ValueTask.CompletedTask;

		_appender.Flush();
		LastCommittedPosition = _lastLogPosition;

		return ValueTask.CompletedTask;
	}

	private static string GetStreamEventType(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? streamName : streamName[..dashIndex];
	}
}
