// // Copyright (C) Ubiquitous AS. All rights reserved
// // Licensed under the Apache License, Version 2.0.
//
// // ReSharper disable CoVariantArrayConversion
//
// using System.Runtime.CompilerServices;
// using System.Runtime.Serialization;
// using System.Xml;
// using EventStore.Core.Data;
// using EventStore.Core.Services.Transport.Grpc;
// using EventStore.Streaming;
// using EventStore.Streaming.Consumers;
// using EventStore.Streaming.Producers;
// using EventStore.Streaming.Readers;
// using Eventuous.Tools;
// using Microsoft.AspNetCore.Mvc.Diagnostics;
// using Microsoft.Extensions.Logging;
// using static Eventuous.Diagnostics.PersistenceEventSource;
//
// namespace Eventuous.EventStore;
//
// /// <summary>
// /// EventStoreDB implementation of <see cref="IEventStore"/>
// /// </summary>
// public class EsdbSystemEventStore : IEventStore {
//     public EsdbSystemEventStore(
//         SystemReader reader,
//         SystemProducer producer,
//         ILogger<EsdbSystemEventStore>? logger
//     ) {
//         Reader   = reader;
//         Producer = producer;
//         _logger  = logger;
//     }
//     
//     SystemReader                   Reader   { get; }
//     SystemProducer                 Producer { get; }
//     ILogger<EsdbSystemEventStore>? _logger  { get; }
//
//     /// <inheritdoc/>
//     public async Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken) {
//         EventStoreRecord? result = await Reader.ReadBackwards(ConsumeFilter.Streams(stream), 1, cancellationToken: cancellationToken).FirstOrDefaultAsync(cancellationToken);
//
//         return result != null || result != EventStoreRecord.None;
//     }
//
//     /// <inheritdoc/>
//     public Task<AppendEventsResult> AppendEvents(
//         StreamName                       stream,
//         ExpectedStreamVersion            expectedVersion,
//         IReadOnlyCollection<StreamEvent> events,
//         CancellationToken                cancellationToken
//     ) {
//         var proposedEvents = events.Select(ToEventData);
//
//         var resultTask = expectedVersion == ExpectedStreamVersion.NoStream
//             ? _client.AppendToStreamAsync(stream, StreamState.NoStream, proposedEvents, cancellationToken: cancellationToken)
//             : AnyOrNot(
//                 expectedVersion,
//                 () => _client.AppendToStreamAsync(stream, StreamState.Any, proposedEvents, cancellationToken: cancellationToken),
//                 () => _client.AppendToStreamAsync(stream, expectedVersion.AsStreamRevision(), proposedEvents, cancellationToken: cancellationToken)
//             );
//
//         return TryExecute(
//             async () => {
//                 var result = await resultTask.NoContext();
//
//                 return new AppendEventsResult(
//                     result.LogPosition.CommitPosition,
//                     result.NextExpectedStreamRevision.ToInt64()
//                 );
//             },
//             stream,
//             () => new ErrorInfo("Unable to appends events to {Stream}", stream),
//             (s, ex) => {
//                 Log.UnableToAppendEvents(stream, ex);
//                 return new AppendToStreamException(s, ex);
//             }
//         );
//
//         EventData ToEventData(StreamEvent streamEvent) {
//             var (eventType, contentType, payload) = _serializer.SerializeEvent(streamEvent.Payload!);
//
//             return new EventData(
//                 Uuid.FromGuid(streamEvent.Id),
//                 eventType,
//                 payload,
//                 _metaSerializer.Serialize(streamEvent.Metadata),
//                 contentType
//             );
//         }
//     }
//
//     /// <inheritdoc/>
//     public Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken) {
//         var read = _client.ReadStreamAsync(Direction.Forwards, stream, start.AsStreamPosition(), count, cancellationToken: cancellationToken);
//
//         return TryExecute(
//             async () => {
//                 var resolvedEvents = await read.ToArrayAsync(cancellationToken).NoContext();
//                 return ToStreamEvents(resolvedEvents);
//             },
//             stream,
//             () => new ErrorInfo("Unable to read {Count} starting at {Start} events from {Stream}", count, start, stream),
//             (s, ex) => new ReadFromStreamException(s, ex)
//         );
//     }
//
//     /// <inheritdoc/>
//     public Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, int count, CancellationToken cancellationToken) {
//         var read = _client.ReadStreamAsync(
//             Direction.Backwards,
//             stream,
//             StreamPosition.End,
//             count,
//             resolveLinkTos: true,
//             cancellationToken: cancellationToken
//         );
//
//         return TryExecute(
//             async () => {
//                 var resolvedEvents = await read.ToArrayAsync(cancellationToken).NoContext();
//                 return ToStreamEvents(resolvedEvents);
//             },
//             stream,
//             () => new ErrorInfo("Unable to read {Count} events backwards from {Stream}", count, stream),
//             (s, ex) => new ReadFromStreamException(s, ex)
//         );
//     }
//
//     /// <inheritdoc/>
//     public Task TruncateStream(
//         StreamName stream, StreamTruncatePosition truncatePosition, 
//         ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken
//     ) =>
//         throw new NotImplementedException();
//
//     /// <inheritdoc/>
//     public Task DeleteStream(StreamName stream, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken) =>
//         throw new NotImplementedException();
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     async Task<T> TryExecute<T>(
//         Func<Task<T>>                      func,
//         string                             stream,
//         Func<ErrorInfo>                    getError,
//         Func<string, Exception, Exception> getException
//     ) {
//         try {
//             return await func().NoContext();
//         }
//         catch (StreamNotFoundException) {
//             _logger?.LogWarning("Stream {Stream} not found", stream);
//             throw new StreamNotFound(stream);
//         }
//         catch (Exception ex) {
//             var (message, args) = getError();
//             // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
//             _logger?.LogWarning(ex, message, args);
//             throw getException(stream, ex);
//         }
//     }
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     static Task<T> AnyOrNot<T>(ExpectedStreamVersion version, Func<Task<T>> whenAny, Func<Task<T>> otherwise)
//         => version == ExpectedStreamVersion.Any ? whenAny() : otherwise();
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     StreamEvent ToStreamEvent(ResolvedEvent resolvedEvent) {
//         var deserialized = _serializer.DeserializeEvent(
//             resolvedEvent.Event.Data.Span,
//             resolvedEvent.Event.EventType,
//             resolvedEvent.Event.ContentType
//         );
//
//         return deserialized switch {
//             SuccessfullyDeserialized success => AsStreamEvent(success.Payload),
//             FailedToDeserialize failed => throw new SerializationException(
//                 $"Can't deserialize {resolvedEvent.Event.EventType}: {failed.Error}"
//             ),
//             _ => throw new SerializationException("Unknown deserialization result")
//         };
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         Metadata? DeserializeMetadata() {
//             var meta = resolvedEvent.Event.Metadata.Span;
//
//             try {
//                 return meta.Length == 0 ? null : _metaSerializer.Deserialize(meta);
//             }
//             catch (MetadataDeserializationException e) {
//                 _logger?.LogWarning(
//                     e,
//                     "Failed to deserialize metadata at {Stream}:{Position}",
//                     resolvedEvent.Event.EventStreamId,
//                     resolvedEvent.Event.EventNumber
//                 );
//
//                 return null;
//             }
//         }
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         StreamEvent AsStreamEvent(object payload)
//             => new(
//                 resolvedEvent.Event.EventId.ToGuid(),
//                 payload,
//                 DeserializeMetadata() ?? new Metadata(),
//                 resolvedEvent.Event.ContentType,
//                 resolvedEvent.Event.EventNumber.ToInt64()
//             );
//     }
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     StreamEvent[] ToStreamEvents(ResolvedEvent[] resolvedEvents)
//         => resolvedEvents
//             .Where(x => !x.Event.EventType.StartsWith("$"))
//             // ReSharper disable once ConvertClosureToMethodGroup
//             .Select(e => ToStreamEvent(e))
//             .ToArray();
//
//     record ErrorInfo(string Message, params object[] Args);
// }