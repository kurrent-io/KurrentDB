using System.Runtime.CompilerServices;
using EventStore.Streaming;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Schema;
using Eventuous;
using ContentType = System.Net.Mime.MediaTypeNames.Application;

namespace EventStore.Extensions.Connectors.Tests;

public partial class StreamingFixture {
    public string NewStreamId([CallerMemberName] string? name = null) =>
        $"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();

    public string NewProcessorId(string? prefix = null) =>
        prefix is null ? $"{GenerateShortId()}-prx" : $"{prefix.Underscore()}-{GenerateShortId()}-prx";

    public SendRequest GenerateTestSendRequest(
        string streamId, int batchSize = 3, SchemaDefinitionType schemaType = SchemaDefinitionType.Json
    ) {
        var messages = Enumerable.Range(1, batchSize)
            .Select(
                sequence => {
                    var entityId = Guid.NewGuid();
                    return Message.Builder
                        .Value(new TestEvent(entityId, sequence))
                        .Key(PartitionKey.From(entityId))
                        .WithSchemaType(schemaType)
                        .Create();
                }
            )
            .ToArray();

        var request = SendRequest.Builder
            .Messages(messages)
            .Stream(streamId)
            .Create();

        return request;
    }

    public List<SendRequest> GenerateTestSendRequests(
        string streamId, int numberOfRequests = 1, int batchSize = 3,
        SchemaDefinitionType schemaType = SchemaDefinitionType.Json
    ) =>
        Enumerable.Range(1, numberOfRequests)
            .Select(_ => GenerateTestSendRequest(streamId, batchSize, schemaType))
            .ToList();

    public async Task<List<SendResult>> ProduceTestEvents(
        string streamId, int numberOfRequests = 1, int batchSize = 3,
        SchemaDefinitionType schemaType = SchemaDefinitionType.Json
    ) {
        var requests = GenerateTestSendRequests(streamId, numberOfRequests, batchSize, schemaType);

        var results = new List<SendResult>();

        foreach (var request in requests)
            results.Add(await Producer.Send(request));

        return results;
    }

    public StreamEvent CreateStreamEvent(int position = default) {
        return new(
            Guid.NewGuid(),
            new TestEvent(),
            new Metadata(),
            ContentType.Json,
            position // position is not used
        );
    }

    public IEnumerable<StreamEvent> CreateStreamEvents(int count) {
        for (var i = 0; i < count; i++)
            yield return CreateStreamEvent(count);
    }

    public StreamName NewStreamName() => new(NewStreamId());
}

public record TestEvent(Guid EntityId = default, int Sequence = 1);