// ReSharper disable CheckNamespace

using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using EventStore.Streaming.Transformers;
using StreamRevision = EventStore.Streaming.StreamRevision;

namespace EventStore.Connect.Consumers;

public static class ConsumeFilterExtensions {
    public static IEventFilter ToEventFilter(this ConsumeFilter filter) {
        if (filter is { IsEmptyFilter: true } or { IsJsonPathFilter: true })
            return NoOpEventFilter.Instance;

        return filter switch {
            { IsStreamIdFilter: true }                       => EventFilter.StreamName.Prefixes(true, filter.Prefixes[0]),
            { IsStreamFilter  : true, IsPrefixFilter: true } => EventFilter.StreamName.Prefixes(true, filter.Prefixes),
            { IsStreamFilter  : true, IsRegexFilter : true } => EventFilter.StreamName.Regex(true, filter.RegularExpression.ToString()),
            { IsRecordFilter  : true, IsPrefixFilter: true } => EventFilter.EventType.Prefixes(true, filter.Prefixes),
            { IsRecordFilter  : true, IsRegexFilter : true } => EventFilter.EventType.Regex(true, filter.RegularExpression.ToString()),
            _                                                => throw new ArgumentOutOfRangeException(nameof(filter), "Invalid consume filter.")
        };
    }

    sealed class NoOpEventFilter : IEventFilter {
        public static readonly NoOpEventFilter Instance = new();

        public bool IsEventAllowed(EventRecord eventRecord) => true;
    }
}

public static class ResolvedEventExtensions {
    public static async ValueTask<EventStoreRecord> ToTransformedRecord(
        this ResolvedEvent resolvedEvent,
        ITransformer transformer,
        Func<SequenceId> nextSequenceId
    ) {
        var headers = Headers.Decode(resolvedEvent.OriginalEvent.Metadata);

        // To support legacy events that do not contain system headers for schema information.
        if (!headers.ContainsKey(HeaderKeys.SchemaType)) {
            SchemaInfo
                .FromContentType(resolvedEvent.OriginalEvent.EventType,
                    resolvedEvent.OriginalEvent.IsJson
                        ? "application/json"
                        : "application/octet-stream") //TODO SS: fix magic strings
                .InjectIntoHeaders(headers);
        }

        var transformResult = await transformer.Transform(resolvedEvent.OriginalEvent.Data, headers);

        if (transformResult is not TransformedRecord transformedRecord)
            return EventStoreRecord.None;

        // Ignore any system-reserved headers that may have been modified by the transform.
        // We allow schema information headers to be modified, however.
        foreach (var transformedHeader in transformedRecord.Headers.Where(transformedHeader =>
                     transformedHeader.Key is HeaderKeys.SchemaType or HeaderKeys.SchemaSubject
                  || !HeaderKeys.All.Contains(transformedHeader.Key))) {
            headers[transformedHeader.Key] = transformedHeader.Value;
        }

        var schemaInfo = SchemaInfo.FromHeaders(headers);

        return resolvedEvent.ToRecord(headers,
            schemaInfo,
            transformedRecord.Data,
            transformedRecord.Value,
            nextSequenceId) with {
            IsTransformed = true
        };
    }

    public static async ValueTask<EventStoreRecord> ToRecord(
        this ResolvedEvent resolvedEvent,
        Deserialize deserialize,
        Func<SequenceId> nextSequenceId
    ) {
        // For now headers will always be encoded as json which makes it easier and more consistent to work with.
        // We can even check the keys in the admin ui for debugging purposes out of the box.

        var headers = Headers.Decode(resolvedEvent.OriginalEvent.Metadata);

        // Handle backwards compatibility with old schema by injecting the legacy schema in the headers.
        // The legacy schema is generated using the event type and content type from the resolved event.

        var schemaInfo = headers.ContainsKey(HeaderKeys.SchemaSubject)
            ? SchemaInfo.FromHeaders(headers)
            : SchemaInfo.FromContentType(resolvedEvent.OriginalEvent.EventType,
                resolvedEvent.OriginalEvent.IsJson
                    ? "application/json"
                    : "application/octet-stream" //TODO SS: fix magic strings
            ).InjectIntoHeaders(headers);

        var data  = resolvedEvent.OriginalEvent.Data;
        var value = await deserialize(data, headers);

        return resolvedEvent.ToRecord(headers,
            schemaInfo,
            data,
            value,
            nextSequenceId);
    }

    // public static async ValueTask<EventStoreRecord> MySSToRecord(this ResolvedEvent resolvedEvent, Deserialize deserialize, Func<SequenceId> nextSequenceId) {
    //     // for now headers will always be encoded as json.
    //     // makes it easier and more consistent to work with.
    //     // we can even check the keys in the admin ui for
    //     // debugging purposes out of the box.
    //
    //     var headers = Headers.Decode(resolvedEvent.OriginalEvent.Metadata);
    //
    //     // handle backwards compatibility with old schema
    //     // by injecting the legacy schema in the headers.
    //     // the legacy schema is generated using the event
    //     // type and content type from the resolved event.
    //     var schema = headers.ContainsKey(HeaderKeys.SchemaSubject)
    //         ? SchemaInfo.FromHeaders(headers)
    //         : SchemaInfo.FromContentType(
    //             resolvedEvent.OriginalEvent.EventType,
    //             resolvedEvent.OriginalEvent.IsJson ?  "application/json" : "application/octet-stream" //TODO SS: fix magic strings
    //         ).InjectIntoHeaders(headers);
    //
    //     var value = await deserialize(resolvedEvent.Event.Data, headers);
    //
    //     var position = RecordPosition.ForStream(
    //         StreamId.From(resolvedEvent.OriginalEvent.EventStreamId),
    //         StreamRevision.From(resolvedEvent.OriginalEvent.EventNumber),
    //         LogPosition.From(
    //             resolvedEvent.OriginalPosition!.Value.CommitPosition,
    //             resolvedEvent.OriginalPosition!.Value.PreparePosition
    //         )
    //     );
    //
    //     var isRedacted = resolvedEvent.OriginalEvent.Flags
    //         .HasAllOf(PrepareFlags.IsRedacted);
    //
    //     var record = new EventStoreRecord {
    //         Id         = RecordId.From(resolvedEvent.OriginalEvent.EventId),
    //         Position   = position,
    //         Timestamp  = resolvedEvent.OriginalEvent.TimeStamp,
    //         SequenceId = nextSequenceId(),
    //         Headers    = headers,
    //         SchemaInfo = schema,
    //         Value      = value!,
    //         ValueType  = value is not null ? value.GetType() : SchemaRegistry.MissingType,
    //         Data       = resolvedEvent.Event.Data,
    //         IsRedacted = isRedacted
    //     };
    //
    //     return record;
    // }

    public static ValueTask<EventStoreRecord> ToRecord(this ResolvedEvent resolvedEvent, Deserialize deserialize, int nextSequenceId) =>
        resolvedEvent.ToRecord(deserialize, () => SequenceId.From((ulong)nextSequenceId));

    static EventStoreRecord ToRecord(
        this ResolvedEvent resolvedEvent,
        Headers headers,
        SchemaInfo schemaInfo,
        ReadOnlyMemory<byte> data,
        object? value,
        Func<SequenceId> nextSequenceId
    ) {
        var position = RecordPosition.ForStream(
            StreamId.From(resolvedEvent.OriginalEvent.EventStreamId),
            StreamRevision.From(resolvedEvent.OriginalEvent.EventNumber),
            LogPosition.From(resolvedEvent.OriginalPosition!.Value.CommitPosition, resolvedEvent.OriginalPosition!.Value.PreparePosition)
        );

        var isRedacted = resolvedEvent.OriginalEvent.Flags
            .HasAllOf(PrepareFlags.IsRedacted);

        var record = new EventStoreRecord {
            Id         = RecordId.From(resolvedEvent.OriginalEvent.EventId),
            Position   = position,
            Timestamp  = resolvedEvent.OriginalEvent.TimeStamp,
            SequenceId = nextSequenceId(),
            Headers    = headers,
            SchemaInfo = schemaInfo,
            Value      = value!,
            ValueType  = value is not null ? value.GetType() : SchemaRegistry.MissingType,
            Data       = data,
            IsRedacted = isRedacted
        };

        return record;
    }
}

public static class RecordPositionExtensions {
    public static Position? ToPosition(this RecordPosition position) =>
        position.LogPosition.ToPosition();

    static Position? ToPosition(this LogPosition position) {
        if (position == LogPosition.Earliest)
            return null;

        if (position == LogPosition.Latest)
            return Position.End;

        return new Position(position.CommitPosition!.Value, position.PreparePosition!.Value);
    }
}