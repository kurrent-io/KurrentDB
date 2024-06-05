// ReSharper disable CheckNamespace

using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;

namespace EventStore.Streaming.Consumers;

public static class ConsumeFilterExtensions {
	public static IEventFilter ToEventFilter(this ConsumeFilter filter) =>
		filter.Scope switch {
			ConsumeFilterScope.Stream when filter.HasPrefixes  => EventFilter.StreamName.Prefixes(true, filter.Prefixes),
			ConsumeFilterScope.Stream when !filter.HasPrefixes => EventFilter.StreamName.Regex(true, filter.RegularExpression.ToString()),
			ConsumeFilterScope.Record when filter.HasPrefixes  => EventFilter.EventType.Prefixes(true, filter.Prefixes),
			ConsumeFilterScope.Record when !filter.HasPrefixes => EventFilter.EventType.Regex(true, filter.RegularExpression.ToString()),
			_                                                  => throw new ArgumentOutOfRangeException(nameof(filter))
		};
}

public static class ResolvedEventExtensions {
	public static async ValueTask<EventStoreRecord> ToRecord(this ResolvedEvent resolvedEvent, Deserialize deserialize, Func<SequenceId> nextSequenceId) {
		// for now headers will always be encoded as json.
		// makes it easier and more consistent to work with.
		// we can even check the keys in the admin ui for
		// debugging purposes out of the box.

		var headers = Headers.Decode(resolvedEvent.OriginalEvent.Metadata);

		// handle backwards compatibility with old schema
		// by injecting the legacy schema in the headers.
		// the legacy schema is generated using the event
		// type and content type from the resolved event.
		var schema = headers.ContainsKey(HeaderKeys.SchemaSubject)
			? SchemaInfo.FromHeaders(headers)
			: SchemaInfo.FromContentType(
				resolvedEvent.OriginalEvent.EventType,
				resolvedEvent.OriginalEvent.IsJson ? "application/json" : "application/octet-stream"
			).InjectIntoHeaders(headers);

		var value = await deserialize(resolvedEvent.Event.Data, headers);

		var position = new RecordPosition {
			StreamId       = StreamId.From(resolvedEvent.OriginalEvent.EventStreamId),
			StreamRevision = StreamRevision.From(resolvedEvent.OriginalEvent.EventNumber),
			LogPosition    = LogPosition.From(
				resolvedEvent.OriginalPosition!.Value.CommitPosition,
				resolvedEvent.OriginalPosition!.Value.PreparePosition
			)
		};

		var isRedacted = resolvedEvent.OriginalEvent.Flags
			.HasAllOf(PrepareFlags.IsRedacted);

		var record = new EventStoreRecord {
			Id         = RecordId.From(resolvedEvent.OriginalEvent.EventId),
			Position   = position,
			Timestamp  = resolvedEvent.OriginalEvent.TimeStamp,
			SequenceId = nextSequenceId(),
			Headers    = headers,
			SchemaInfo = schema,
			Value      = value!,
			Data       = resolvedEvent.Event.Data,
			IsRedacted = isRedacted
		};

		return record;
	}

	public static async ValueTask<EventStoreRecord> ToRecord(this ResolvedEvent resolvedEvent, Deserialize deserialize, int nextSequenceId) =>
		await resolvedEvent.ToRecord(deserialize, () => SequenceId.From((ulong)nextSequenceId));
}

public static class RecordPositionExtensions {
	public static Position? ToPosition(this RecordPosition position) {
		return position == RecordPosition.Unset || position.LogPosition == LogPosition.Earliest
			? null
			: position.LogPosition == LogPosition.Latest
				? Position.End
				: new Position(position.LogPosition.CommitPosition!.Value, position.LogPosition.PreparePosition!.Value);
	}

	public static Position ToPosition2(this RecordPosition position) {
		return position == RecordPosition.Unset || position.LogPosition == LogPosition.Earliest
			? Position.Start
			: position.LogPosition == LogPosition.Latest
				? Position.End
				: new(position.LogPosition.CommitPosition!.Value, position.LogPosition.PreparePosition!.Value);
	}
}