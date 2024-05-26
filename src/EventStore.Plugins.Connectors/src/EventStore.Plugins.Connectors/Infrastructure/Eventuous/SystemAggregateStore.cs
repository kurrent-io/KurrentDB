// using EventStore.Core.Data;
// using EventStore.Core.Services.Transport.Grpc;
// using EventStore.Streaming;
// using EventStore.Streaming.Producers;
// using EventStore.Streaming.Schema;
// using EventStore.Streaming.Schema.Serializers;
// using EventStore.Core.Client;
// using Eventuous;
// using StreamRevision = EventStore.Core.Services.Transport.Common.StreamRevision;
//
// namespace EventStore.Connectors.Infrastructure.Eventuous;
//
// public class SystemEventReader : IEventReader {
//     public SystemEventReader(SystemClient client) {
//         Client = client;
//     }
//
//     SystemClient Client { get; }
//
//     public async Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken) {
//         var result = await Client.Reading
//             .ReadStreamForwards(stream, StreamRevision.FromInt64(start.Value), count, cancellationToken)
//             .Select(x => new StreamEvent(x.Event.EventId, x.Event.Data, new Metadata(), "", x.Event.LogPosition))
//             .ToArrayAsync(cancellationToken);
//
//         return result;
//     }
//
//     public async Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, int count, CancellationToken cancellationToken) {
//         var result = await Client.Reading
//             .ReadStreamBackwards(stream, StreamRevision.End, count, cancellationToken)
//             .Select(x => new StreamEvent(x.Event.EventId, x.Event.Data, new Metadata(), "", x.Event.LogPosition))
//             .ToArrayAsync(cancellationToken);
//
//         return result;
//     }
// }
//
// public class SystemEventWriter : IEventWriter {
//     public SystemEventWriter(SystemClient client) {
//         Client    = client;
//         Serialize = (value, headers) => SchemaRegistry.Global.As<ISchemaSerializer>().Serialize(value, headers);
//     }
//
//     SystemClient Client    { get; }
//     Serialize    Serialize { get; }
//
//     public async Task<AppendEventsResult> AppendEvents(StreamName stream, ExpectedStreamVersion expectedVersion, IReadOnlyCollection<StreamEvent> events, CancellationToken cancellationToken) {
//         var internalEvents = await ToEvents(
//             events, headers => headers.Add(HeaderKeys.ProducerName, "SystemEventWriter"), Serialize
//         );
//         
//         var result = await Client.Writing.WriteEvents(
//             stream, 
//             internalEvents,
//             StreamRevision.FromInt64(expectedVersion.Value).ToInt64(), // what?! lol 
//             cancellationToken
//         );
//
//         return new(
//             result.Position.CommitPosition,
//             result.StreamRevision.ToInt64()
//         );
//     }
//     
//     static ValueTask<Event[]> ToEvents(IReadOnlyCollection<StreamEvent> events,  Action<Metadata> configureHeaders, Serialize serialize) {
//         return events
//             .ToAsyncEnumerable()
//             .SelectAwait(async msg => {
//                 configureHeaders(msg.Metadata);
//                 return await Map(msg, serialize);
//             })
//             .ToArrayAsync();
//
//         static async Task<Event> Map(StreamEvent streamEvent, Serialize serialize) {
//             var headers = new Headers(streamEvent.Metadata.ToDictionary(x => x.Key, x => x.Value?.ToString()));
//             var data    = await serialize(streamEvent.Payload, headers);
//
//             var eventId  = Uuid.FromGuid(streamEvent.Id).ToGuid(); // not sure if needed...sanity check?!
//             var schema   = SchemaInfo.FromHeaders(headers);
//             var metadata = Headers.Encode(headers);
//             var isJson   = schema.SchemaType == SchemaDefinitionType.Json;
//             
//             return new(
//                 eventId, 
//                 schema.Subject, 
//                 isJson, 
//                 data.ToArray(), 
//                 metadata.ToArray()
//             );
//         }
//     }
// }
//
// public class SystemEventWriter2 : IEventWriter {
//     public SystemEventWriter2(SystemProducer client) {
//         Client    = client;
//         Serialize = (value, headers) => SchemaRegistry.Global.As<ISchemaSerializer>().Serialize(value, headers);
//     }
//
//     SystemProducer Client    { get; }
//     Serialize      Serialize { get; }
//
//     public async Task<AppendEventsResult> AppendEvents(StreamName stream, ExpectedStreamVersion expectedVersion, IReadOnlyCollection<StreamEvent> events, CancellationToken cancellationToken) {
//         var internalEvents = await ToEvents(
//             events, headers => headers.Add(HeaderKeys.ProducerName, "SystemEventWriter"), Serialize
//         );
//
//         var request = SendRequest.Builder
//             .Stream(stream)
//             .Messages()
//             .ExpectedStreamRevision(expectedVersion.Value)
//             .Create();
//         
//         var result = await Client.Send()
//
//         return new(
//             result.Position.CommitPosition,
//             result.StreamRevision.ToInt64()
//         );
//     }
//     
//     static ValueTask<Event[]> ToEvents(IReadOnlyCollection<StreamEvent> events,  Action<Metadata> configureHeaders, Serialize serialize) {
//         return events
//             .ToAsyncEnumerable()
//             .SelectAwait(async msg => {
//                 configureHeaders(msg.Metadata);
//                 return await Map(msg, serialize);
//             })
//             .ToArrayAsync();
//
//         static async Task<Event> Map(StreamEvent streamEvent, Serialize serialize) {
//             var headers = new Headers(streamEvent.Metadata.ToDictionary(x => x.Key, x => x.Value?.ToString()));
//             var data    = await serialize(streamEvent.Payload, headers);
//
//             var eventId  = Uuid.FromGuid(streamEvent.Id).ToGuid(); // not sure if needed...sanity check?!
//             var schema   = SchemaInfo.FromHeaders(headers);
//             var metadata = Headers.Encode(headers);
//             var isJson   = schema.SchemaType == SchemaDefinitionType.Json;
//             
//             return new(
//                 eventId, 
//                 schema.Subject, 
//                 isJson, 
//                 data.ToArray(), 
//                 metadata.ToArray()
//             );
//         }
//     }
// }