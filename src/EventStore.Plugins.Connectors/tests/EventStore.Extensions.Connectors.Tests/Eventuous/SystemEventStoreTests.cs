#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.

using EventStore.Connectors.Eventuous;
using EventStore.Streaming;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using Eventuous;
using Shouldly;

namespace EventStore.Extensions.Connectors.Tests.Eventuous;

[Trait("Category", "Integration")]
public class SystemEventStoreTests(ITestOutputHelper output, StreamingFixture fixture)
    : StreamingTests(output, fixture) {
    [Fact]
    public async Task stream_does_not_exists() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);
        var stream     = Fixture.NewStreamName();

        // Act
        var exists = await eventstore.StreamExists(stream);

        // Assert
        exists.ShouldBe(false);
    }

    [Fact]
    public async Task stream_exists() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);
        var stream     = Fixture.NewStreamName();

        // Act
        await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.NoStream,
            Fixture.CreateStreamEvents(5).ToArray()
        );

        var exists = await eventstore.StreamExists(stream);

        // Assert
        exists.ShouldBe(true);
    }

    [Fact]
    public async Task read_non_existent_stream_forward_with_count() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = Fixture.NewStreamName();

        // Assert
        await eventstore
            .ReadEvents(stream, StreamReadPosition.Start, 1, cancellator.Token)
            .ShouldNotThrowAsync();
    }

    [Fact]
    public async Task read_nonexistent_stream_forwards_without_count() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = Fixture.NewStreamName();

        await eventstore.ReadEvents(stream, StreamReadPosition.Start, 0, cancellator.Token)
            .ShouldThrowAsync<ReadFromStreamException>();
    }

    [Fact]
    public async Task read_nonexistent_stream_backwards_without_count() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = Fixture.NewStreamName();

        // Assert
        await eventstore
            .ReadEventsBackwards(stream, 0, cancellator.Token)
            .ShouldThrowAsync<ReadFromStreamException>();
    }

    [Fact]
    public async Task appends_single() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(Fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(Fixture.Publisher)
            .Create();

        var eventStore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream      = Fixture.NewStreamName();
        var streamEvent = Fixture.CreateStreamEvent();

        // Act
        var appendResult = await eventStore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            new[] { streamEvent },
            cancellator.Token
        );

        // Assert
        appendResult.ShouldNotBeNull();
        appendResult.GlobalPosition.ShouldBeGreaterThan<ulong>(0);

        var readResults = await eventStore
            .ReadEvents(
                stream,
                StreamReadPosition.Start,
                1,
                cancellator.Token
            );

        // Assert
        Assert.Single(readResults);
    }

    [Fact]
    public async Task appends_many() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(Fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(Fixture.Publisher)
            .Create();

        var eventStore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream       = Fixture.NewStreamName();
        var streamEvents = Fixture.CreateStreamEvents(5).ToArray();

        // Act
        var appendResult = await eventStore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            streamEvents,
            cancellator.Token
        );

        // Assert
        appendResult.ShouldNotBeNull();
        appendResult.GlobalPosition.ShouldBeGreaterThan<ulong>(0);

        var readResults = await eventStore
            .ReadEvents(
                stream,
                StreamReadPosition.Start,
                streamEvents.Length,
                cancellator.Token
            );

        // Assert
        readResults.Length.ShouldBe(5);
    }

    [Fact]
    public async Task read_events_backwards() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = Fixture.NewStreamName();

        // Act
        var events = Fixture
            .CreateStreamEvents(2)
            .ToArray();

        var appendResult = await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            events,
            cancellator.Token
        );

        // Assert
        appendResult.ShouldNotBeNull();
        appendResult.GlobalPosition.ShouldBeGreaterThan<ulong>(0);

        var readResults = await eventstore.ReadEventsBackwards(stream, 2, cancellator.Token);

        readResults.Length.ShouldBe(2);
        events.First().Id.ShouldBe(readResults.Last().Id);
        events.Last().Id.ShouldBe(readResults.First().Id);
    }

    [Fact]
    public async Task read_from_empty_stream() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = Fixture.NewStreamName();

        // Act
        var readResults = await eventstore.ReadEvents(stream, StreamReadPosition.Start, 1, cancellator.Token);

        // Assert
        readResults.ShouldBeEmpty();
    }

    [Fact]
    public async Task read_more_events_than_exist() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream       = Fixture.NewStreamName();
        var streamEvents = Fixture.CreateStreamEvents(2).ToArray();

        await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            streamEvents,
            cancellator.Token
        );

        // Act
        var readResults = await eventstore.ReadEvents(stream, StreamReadPosition.Start, 10, cancellator.Token);

        // Assert
        readResults.Length.ShouldBe(2);
    }

    [Fact]
    public async Task append_duplicate_events() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        var stream = Fixture.NewStreamName();

        var duplicateEvent = Fixture.CreateStreamEvent();

        // Assert
        await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            new[] { duplicateEvent, duplicateEvent }
        ).ShouldNotThrowAsync();
    }

    [Fact]
    public async Task append_duplicate_events_to_same_stream_using_multiple_operations() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);

        var stream = Fixture.NewStreamName();

        var duplicateEvent = Fixture.CreateStreamEvent();

        // Assert
        await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            new[] { duplicateEvent }
        ).ShouldNotThrowAsync();

        await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            new[] { duplicateEvent }
        ).ShouldNotThrowAsync();
    }

    [Fact]
    public async Task multiple_idempotent_writes() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);
        var stream     = Fixture.NewStreamName();

        var events = Fixture.CreateStreamEvents(4).ToArray();

        // Assert
        var appendResult = await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            events
        );

        appendResult.NextExpectedVersion.ShouldBe(3);

        appendResult = await eventstore.AppendEvents(
            stream,
            new ExpectedStreamVersion(0),
            events
        );

        appendResult.NextExpectedVersion.ShouldBe(3);
    }

    [Fact]
    public async Task multiple_writes_with_same_event_ids() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var eventstore = new SystemEventStore(reader, producer);
        var stream     = Fixture.NewStreamName();

        var events = Enumerable
            .Repeat(Fixture.CreateStreamEvent(), 6)
            .ToArray();

        // Assert
        var appendResult = await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            events
        );

        appendResult.NextExpectedVersion.ShouldBe(5);

        appendResult = await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            events
        );

        appendResult.NextExpectedVersion.ShouldBe(0);
    }

    [Fact]
    public async Task append_event_with_all_arguments_filled() {
        // Arrange
        var reader = SystemReader.Builder
            .Publisher(fixture.Publisher)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(fixture.Publisher)
            .Create();

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var eventstore = new SystemEventStore(reader, producer);
        var stream     = Fixture.NewStreamName();

        var metadataInfo = new Dictionary<string, object?> {
            { "Key1", "Value1" },
            { "Key2", 12345 },
            { "Key3", true }
        };

        Metadata metadata = new(metadataInfo);

        var evt = Fixture.CreateStreamEvent() with {
            Metadata = metadata
        };

        // Act
        var appendResult = await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            new[] { evt },
            cancellator.Token
        );

        appendResult.ShouldNotBeNull();
        appendResult.GlobalPosition.ShouldBeGreaterThan<ulong>(0);

        var readResults = await eventstore
            .ReadEvents(
                stream,
                StreamReadPosition.Start,
                1,
                cancellator.Token
            );

        // Assert
        readResults.Length.ShouldBe(1);
        var metadataResults = readResults.First().Metadata;

        var expectedMetadata = new Dictionary<string, string> {
            { "Key1", "Value1" },
            { "Key2", "12345" },
            { "Key3", "True" },
            { HeaderKeys.SchemaType, "json" },
            { HeaderKeys.SchemaSubject, typeof(TestEvent).FullName! }
        };

        foreach (var (key, value) in expectedMetadata)
            metadataResults.ShouldContainKeyAndValue(key, value);
    }
}