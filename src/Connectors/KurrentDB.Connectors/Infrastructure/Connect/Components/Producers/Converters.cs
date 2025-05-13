// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.Transport.Grpc;
using Kurrent.Surge;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;

namespace KurrentDB.Connect.Producers;

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
                metadata.ToArray(),
                properties: []
            );
        }
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
