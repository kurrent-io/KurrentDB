// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Protocol.V2.Streams;
using SchemaInfo = Kurrent.Surge.Schema.SchemaInfo;

namespace KurrentDB.Surge.Producers;

public static class ProduceRequestExtensions {
    public static ValueTask<Event[]> ToEvents(this ProduceRequest request, Action<Headers> configureHeaders, Serialize serialize) {
        return request.Messages
            .ToAsyncEnumerable()
            .SelectAwait(async msg => await Map(msg.With(x => configureHeaders(x.Headers)), serialize))
            .ToArrayAsync();

        static async Task<Event> Map(Message message, Serialize serialize) {
            var data = await serialize(message.Value, message.Headers);

            var eventId  = Uuid.FromGuid(message.RecordId).ToGuid(); // not sure if needed...
            var schema   = SchemaInfo.FromHeaders(message.Headers);
            var metadata = Headers.Encode(message.Headers);
            var isJson   = schema.SchemaDataFormat == SchemaDataFormat.Json;

            return new(
                eventId,
                schema.SchemaName,
                isJson,
                data.ToArray(),
                isPropertyMetadata: false,
                metadata.ToArray()
            );
        }
    }

    public static async ValueTask<RepeatedField<AppendRecord>> ToAppendRecords(this ProduceRequest request, Action<Headers> configureHeaders, Serialize serialize) {
        var records = new RepeatedField<AppendRecord>();

        foreach (var msg in request.Messages) {
            var message = msg.With(x => configureHeaders(x.Headers));
            var data = await serialize(message.Value, message.Headers);
            var schema = SchemaInfo.FromHeaders(message.Headers);

            var record = new AppendRecord {
                RecordId = Uuid.FromGuid(message.RecordId).ToString(),
                Schema = new KurrentDB.Protocol.V2.Streams.SchemaInfo {
                    Format = schema.SchemaDataFormat == SchemaDataFormat.Json ? SchemaFormat.Json : SchemaFormat.Bytes,
                    Name = schema.SchemaName
                },
                Data = Google.Protobuf.ByteString.CopyFrom(data.ToArray())
            };

            record.Properties.Add(message.Headers.Map());

            records.Add(record);
        }

        return records;
    }
}

// TODO: Move to Surge
public static class HeadersExtensions {
    public static MapField<string, Value> Map(this Headers headers) {
        var map = new MapField<string, Value>();

        foreach (var (key, value) in headers) {
            map[key] = value switch {
                null => Value.ForNull(),
                not null when bool.TryParse(value, out var boolValue) => Value.ForBool(boolValue),
                not null when long.TryParse(value, out var longValue) => Value.ForNumber(longValue),
                not null when double.TryParse(value, out var doubleValue) => Value.ForNumber(doubleValue),
                _ => Value.ForString(value)
            };
        }

        return map;
    }
}

public static class StreamingErrorConverters {
    public static StreamingError ToProducerStreamingError(this Exception ex, string targetStream) =>
        ex switch {
            ReadResponseException.Timeout        => new RequestTimeoutError(targetStream, ex.Message),
            ReadResponseException.StreamNotFound => new StreamNotFoundError(targetStream),
            ReadResponseException.StreamDeleted  => new StreamDeletedError(targetStream),
            ReadResponseException.AccessDenied   => new StreamAccessDeniedError(targetStream),
            ReadResponseException.WrongExpectedRevision wex => new ExpectedStreamRevisionError(
                targetStream,
                StreamRevision.From(wex.ExpectedStreamRevision.ToInt64()),
                StreamRevision.From(wex.ActualStreamRevision.ToInt64())
            ),
            ReadResponseException.NotHandled.ServerNotReady => new ServerNotReadyError(),
            ReadResponseException.NotHandled.ServerBusy     => new ServerTooBusyError(),
            ReadResponseException.NotHandled.LeaderInfo li  => new ServerNotLeaderError(li.Host, li.Port),
            ReadResponseException.NotHandled.NoLeaderInfo   => new ServerNotLeaderError(),
            _                                               => new StreamingCriticalError(ex.Message, ex)
        };
}
