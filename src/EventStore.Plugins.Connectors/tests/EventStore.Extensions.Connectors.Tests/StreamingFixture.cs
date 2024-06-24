using EventStore.Core.Bus;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using EventStore.Testing;
using EventStore.Testing.Fixtures;

namespace EventStore.Extensions.Connectors.Tests;

[PublicAPI]
public partial class StreamingFixture : ClusterVNodeFixture {
    public StreamingFixture() {
        SchemaRegistry   = SchemaRegistry.Global;
        SchemaSerializer = SchemaRegistry;

        Producer = SystemProducer.Builder
            .ProducerId("streaming-test-producer")
            .Publisher(Publisher)
            .SchemaRegistry(SchemaRegistry)
            //.LoggerFactory(LoggerFactory)
            //.EnableLogging()
            .Create();
    }

    public ISchemaSerializer SchemaSerializer { get; }
    public SchemaRegistry    SchemaRegistry   { get; }
    public IProducer         Producer         { get; }
}

// public abstract class StreamingTests(ITestOutputHelper output, StreamingFixture fixture)
// 	: ClusterVNodeTests<StreamingFixture>(output, fixture);

public abstract class StreamingTests<TFixture> : IClassFixture<TFixture> where TFixture : StreamingFixture {
    protected StreamingTests(ITestOutputHelper output, TFixture fixture) =>
        Fixture = fixture.With(x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }

    protected IPublisher  Publisher  => Fixture.Publisher;
    protected ISubscriber Subscriber => Fixture.Subscriber;
}

public abstract class StreamingTests(ITestOutputHelper output, StreamingFixture fixture)
    : StreamingTests<StreamingFixture>(output, fixture);