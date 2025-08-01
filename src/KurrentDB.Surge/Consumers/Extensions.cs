// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.TransactionLog.LogRecords;
using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using StreamRevision = Kurrent.Surge.StreamRevision;

namespace KurrentDB.Connect.Consumers;

public static class ConsumeFilterExtensions {
    public static IEventFilter ToEventFilter(this ConsumeFilter filter) {
        if (filter is { IsEmptyFilter: true } or { IsJsonPathFilter: true })
            return NoOpEventFilter.Instance;

        return filter switch {
            { IsStreamFilter: true } => EventFilter.StreamName.Regex(true, filter.Expression),
            { IsRecordFilter: true } => EventFilter.EventType.Regex(true, filter.Expression),
            _                        => throw new ArgumentOutOfRangeException(nameof(filter), "Invalid consume filter.")
        };
    }

    sealed class NoOpEventFilter : IEventFilter {
        public static readonly NoOpEventFilter Instance = new();

        public bool IsEventAllowed(EventRecord eventRecord) => true;
    }
}

public static class ResolvedEventExtensions {
    public static async ValueTask<SurgeRecord> ToRecord(
        this ResolvedEvent resolvedEvent,
        Deserialize deserialize,
        Func<SequenceId> nextSequenceId
    ) {
        // For now headers will always be encoded as json which makes it easier and more consistent to work with.
        // We can even check the keys in the admin ui for debugging purposes out of the box.

        var headers = Headers.Decode(resolvedEvent.OriginalEvent.Metadata);

        // Handle backwards compatibility with old schema by injecting the legacy schema in the headers.
        // The legacy schema is generated using the event type and content type from the resolved event.

#pragma warning disable CS0618 // Type or member is obsolete
        var schemaInfo = headers.ContainsKey(HeaderKeys.SchemaName) || headers.ContainsKey(HeaderKeys.SchemaName)
#pragma warning restore CS0618 // Type or member is obsolete
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

    // public static async ValueTask<SurgeRecord> MySSToRecord(this ResolvedEvent resolvedEvent, Deserialize deserialize, Func<SequenceId> nextSequenceId) {
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
    //     var record = new SurgeRecord {
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

    public static ValueTask<SurgeRecord> ToRecord(this ResolvedEvent resolvedEvent, Deserialize deserialize, int nextSequenceId) =>
        resolvedEvent.ToRecord(deserialize, () => SequenceId.From((ulong)nextSequenceId));

    static SurgeRecord ToRecord(
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

        var record = new SurgeRecord {
            Id         = RecordId.From(resolvedEvent.OriginalEvent.EventId),
            Position   = position,
            Timestamp  = resolvedEvent.OriginalEvent.TimeStamp,
            SequenceId = nextSequenceId(),
            Headers    = headers,
            SchemaInfo = schemaInfo,
            Value      = value!,
            ValueType  = value is not null ? value.GetType() : Kurrent.Surge.Schema.SchemaRegistry.MissingType,
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
