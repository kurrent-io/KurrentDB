// using Eventuous;
//
// namespace EventStore.Extensions.Connectors.Tests.Eventuous;
//
// public class InMemoryEventStore : IEventStore {
//     Dictionary<string, StreamEvent[]> Streams { get; init; } = new();
//
//     public Task<StreamEvent[]> ReadEvents(
//         StreamName stream,
//         StreamReadPosition start,
//         int count,
//         CancellationToken cancellationToken
//     ) =>
//         Task.FromResult(
//             !Streams.ContainsKey(stream)
//                 ? Array.Empty<StreamEvent>()
//                 : Streams[stream].Skip((int)start.Value).Take(count).ToArray()
//         );
//
//     public Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, int count, CancellationToken cancellationToken) =>
//         Task.FromResult(
//             !Streams.ContainsKey(stream)
//                 ? Array.Empty<StreamEvent>()
//                 : Streams[stream].Reverse().Take(count).ToArray()
//         );
//
//     public Task<AppendEventsResult> AppendEvents(
//         StreamName stream,
//         ExpectedStreamVersion expectedVersion,
//         IReadOnlyCollection<StreamEvent> events,
//         CancellationToken cancellationToken
//     ) {
//         if (!Streams.ContainsKey(stream))
//             Streams[stream] = [];
//
//         Streams[stream] = Streams[stream].Concat(events).ToArray();
//         var streamLength = Streams[stream].Length;
//
//         return Task.FromResult(new AppendEventsResult((ulong)streamLength, streamLength + 1));
//     }
//
//     public Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken) =>
//         Task.FromResult(Streams.ContainsKey(stream));
//
//     public Task TruncateStream(
//         StreamName stream,
//         StreamTruncatePosition truncatePosition,
//         ExpectedStreamVersion expectedVersion,
//         CancellationToken cancellationToken
//     ) {
//         if (!Streams.ContainsKey(stream))
//             return Task.CompletedTask;
//
//         Streams[stream] = Streams[stream][..(int)truncatePosition.Value];
//         return Task.CompletedTask;
//     }
//
//     public Task DeleteStream(
//         StreamName stream,
//         ExpectedStreamVersion expectedVersion,
//         CancellationToken cancellationToken
//     ) {
//         Streams.Remove(stream);
//         return Task.CompletedTask;
//     }
// }