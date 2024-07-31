using System.Runtime.CompilerServices;
using EventStore.Streaming;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Schema;
using Eventuous;

namespace EventStore.Extensions.Connectors.Tests;

public partial class ConnectorsAssemblyFixture {
    public string NewStreamId([CallerMemberName] string? name = null) =>
        $"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();

    public string GenerateShortId()  => Guid.NewGuid().ToString()[30..];
    public string NewConnectorId()   => $"connector-id-{GenerateShortId()}".ToLowerInvariant();
    public string NewConnectorName() => $"connector-name-{GenerateShortId()}".ToLowerInvariant();

    public string NewProcessorId(string? prefix = null) =>
        prefix is null ? $"{GenerateShortId()}-prx" : $"{prefix.Underscore()}-{GenerateShortId()}-prx";

    public ProduceRequest GenerateTestProduceRequest(
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

        var request = ProduceRequest.Builder
            .Messages(messages)
            .Stream(streamId)
            .Create();

        return request;
    }

    public List<ProduceRequest> GenerateTestSendRequests(
        string streamId, int numberOfRequests = 1, int batchSize = 3,
        SchemaDefinitionType schemaType = SchemaDefinitionType.Json
    ) =>
        Enumerable.Range(1, numberOfRequests)
            .Select(_ => GenerateTestProduceRequest(streamId, batchSize, schemaType))
            .ToList();

    public async Task<List<ProduceResult>> ProduceTestEvents(
        string streamId, int numberOfRequests = 1, int batchSize = 3,
        SchemaDefinitionType schemaType = SchemaDefinitionType.Json
    ) {
        var requests = GenerateTestSendRequests(streamId, numberOfRequests, batchSize, schemaType);

        var results = new List<ProduceResult>();

        foreach (var request in requests)
            results.Add(await Producer.Produce(request));

        return results;
    }

    public NewStreamEvent CreateStreamEvent(int position = default) => new(Guid.NewGuid(), new TestEvent(), new Metadata());

    public IEnumerable<NewStreamEvent> CreateStreamEvents(int count = 1) {
        for (var i = 0; i < count; i++)
            yield return CreateStreamEvent(count);
    }

    public StreamName NewStreamName() => new(NewStreamId());
}

public record TestEvent(Guid EntityId = default, int Sequence = 1);