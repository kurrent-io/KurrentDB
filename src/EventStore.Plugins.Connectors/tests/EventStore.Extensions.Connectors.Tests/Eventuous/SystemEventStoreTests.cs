#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.

using EventStore.Connectors.Eventuous;
using EventStore.Streaming;
using Eventuous;
using Shouldly;

namespace EventStore.Extensions.Connectors.Tests.Eventuous;

[Trait("Category", "Integration")]
public class SystemEventStoreTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests<ConnectorsAssemblyFixture>(output, fixture) {
    SystemEventStore NewSystemEventStore() => new SystemEventStore(Fixture.NewReader().Create(), Fixture.NewProducer().Create());

    [Fact]
    public async Task stream_does_not_exists() {
        // Arrange
        var stream = Fixture.NewStreamName();

        var eventstore = NewSystemEventStore();

        // Act
        var exists = await eventstore.StreamExists(stream);

        // Assert
        exists.ShouldBe(false);
    }

    [Fact]
    public async Task stream_exists() {
        // Arrange
        var eventStore = NewSystemEventStore();

        var stream = Fixture.NewStreamName();

        // Act
        await eventStore.AppendEvents(
            stream,
            ExpectedStreamVersion.NoStream,
            Fixture.CreateStreamEvents(5).ToArray()
        );

        var exists = await eventStore.StreamExists(stream);

        // Assert
        exists.ShouldBe(true);
    }

    [Fact]
    public async Task appends_single() {
        // Arrange
        var eventStore = NewSystemEventStore();

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
        var eventStore = NewSystemEventStore();

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = Fixture.NewStreamName();
        var events = Fixture.CreateStreamEvents(5).ToArray();

        // Act
        var appendResult = await eventStore.AppendEvents(stream, ExpectedStreamVersion.Any, events, cancellator.Token);

        // Assert
        appendResult.GlobalPosition.ShouldBeGreaterThan<ulong>(0);

        var readResults = await eventStore
            .ReadEvents(stream, StreamReadPosition.Start, events.Length, cancellator.Token);

        // Assert
        readResults.Length.ShouldBe(5);
    }

    [Fact]
    public async Task append_duplicate_events_with_same_event_ids() {
        // Arrange
        var eventstore = NewSystemEventStore();

        var stream = Fixture.NewStreamName();

        var duplicateEvents = Enumerable.Repeat(Fixture.CreateStreamEvent(), 3).ToArray();

        // Assert
        var result = await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            duplicateEvents
        );

        result.NextExpectedVersion.ShouldBe(duplicateEvents.Length - 1);
    }

    [Fact]
    public async Task append_using_expected_stream_version_throws_wrong_stream_revision_error_when_() {
        // Arrange
        var eventstore = NewSystemEventStore();
        var stream     = Fixture.NewStreamName();

        // Assert
        var result = await eventstore.AppendEvents(
            stream,
            ExpectedStreamVersion.NoStream,
            Fixture.CreateStreamEvents().ToArray()
        );

        result.NextExpectedVersion.ShouldBe(0);

        await eventstore.AppendEvents(
            stream,
            new ExpectedStreamVersion(99),
            Fixture.CreateStreamEvents().ToArray()
        ).ShouldThrowAsync<ExpectedStreamRevisionError>();
    }

    [Fact]
    public async Task append_with_no_stream_expected_version_throws_wrong_stream_revision_error_() {
        // Arrange
        var eventStore = NewSystemEventStore();

        var stream = Fixture.NewStreamName();

        // Assert
        var result = await eventStore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            Fixture.CreateStreamEvents(4).ToArray()
        );

        result.NextExpectedVersion.ShouldBe(3);

        await eventStore.AppendEvents(
                stream,
                ExpectedStreamVersion.NoStream,
                Fixture.CreateStreamEvents().ToArray()
            )
            .ShouldThrowAsync<ExpectedStreamRevisionError>();
    }

    [Fact]
    public async Task multiple_idempotent_writes_with_unique_event_ids() {
        // Arrange
        var eventStore = NewSystemEventStore();

        var stream = Fixture.NewStreamName();

        var events = Fixture.CreateStreamEvents(4).ToArray();

        // Assert
        var appendResult = await eventStore.AppendEvents(stream, ExpectedStreamVersion.Any, events);
        appendResult.NextExpectedVersion.ShouldBe(3);

        appendResult = await eventStore.AppendEvents(stream, ExpectedStreamVersion.Any, events);
        appendResult.NextExpectedVersion.ShouldBe(3);
    }

    [Fact]
    public async Task multiple_idempotent_writes_with_same_event_ids() {
        // Arrange
        var eventStore = NewSystemEventStore();

        var stream = Fixture.NewStreamName();

        var events = Enumerable.Repeat(Fixture.CreateStreamEvent(), 6).ToArray();

        // Assert
        var result = await eventStore.AppendEvents(stream, ExpectedStreamVersion.Any, events);
        result.NextExpectedVersion.ShouldBe(5);

        result = await eventStore.AppendEvents(stream, ExpectedStreamVersion.Any, events);
        result.NextExpectedVersion.ShouldBe(0);
    }

    [Fact]
    public async Task append_with_custom_and_default_headers_are_correctly_parsed() {
        // Arrange
        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var eventStore = NewSystemEventStore();

        var stream = Fixture.NewStreamName();

        var eventMetadata = Fixture.CreateStreamEvent() with {
            Metadata = new(new Dictionary<string, object?> {
                { "Key1", "Value1" },
                { "Key2", 12345 },
                { "Key3", true }
            })
        };

        // Act
        var result = await eventStore.AppendEvents(
            stream,
            ExpectedStreamVersion.Any,
            new[] { eventMetadata },
            cancellator.Token
        );

        result.GlobalPosition.ShouldBeGreaterThan<ulong>(0);

        var readResults = await eventStore
            .ReadEvents(stream, StreamReadPosition.Start, 1, cancellator.Token);

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

        metadataResults.ShouldContainKey(HeaderKeys.ProducerId);

        foreach (var (key, value) in expectedMetadata)
            metadataResults.ShouldContainKeyAndValue(key, value);
    }

    [Fact]
    public async Task read_some_events_forward_from_non_existent_stream() {
        // Arrange
        var eventStore = NewSystemEventStore();

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var stream = Fixture.NewStreamName();

        // Act & Assert
        var ex = await eventStore
            .ReadEvents(stream, StreamReadPosition.Start, 10, cancellator.Token)
            .ShouldThrowAsync<StreamNotFound>();
    }

    // makes no sense except to validate the argument
    // [Fact]
    // public async Task read_zero_events_forward_from_non_existent_stream() {
    //     // Arrange
    //     var eventStore = NewSystemEventStore();
    //
    //     using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    //
    //     var stream = Fixture.NewStreamName();
    //
    //     // Assert
    //     await eventStore
    //         .ReadEvents(stream, StreamReadPosition.Start, 0, cancellator.Token)
    //         .ShouldThrowAsync<StreamNotFoundError>();
    // }

    [Fact(Skip = "Must investigate cause it returns empty stream array instead of SteamNotFound")]
    public async Task read_some_events_backwards_from_nonexistent_stream() {
        // Arrange
        var eventStore = NewSystemEventStore();

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = Fixture.NewStreamName();

        // Act & Assert
        await eventStore
            .ReadEventsBackwards(stream, new(long.MaxValue),  10, cancellator.Token)
            .ShouldThrowAsync<StreamNotFound>();
    }

    [Fact]
    public async Task read_events_backwards() {
        // Arrange
        var eventStore = NewSystemEventStore();

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = Fixture.NewStreamName();

        // Act
        var events = Fixture.CreateStreamEvents(2).ToArray();

        var appendResult = await eventStore.AppendEvents(stream, ExpectedStreamVersion.Any, events, cancellator.Token);

        // Assert
        appendResult.GlobalPosition.ShouldBeGreaterThan<ulong>(0);

        var readResults = await eventStore.ReadEventsBackwards(stream, new(long.MaxValue), 2, cancellator.Token);

        readResults.Length.ShouldBe(2);
        events.First().Id.ShouldBe(readResults.Last().Id);
        events.Last().Id.ShouldBe(readResults.First().Id);
    }

    [Fact]
    public async Task read_events_exceeding_stream_size() {
        // Arrange
        var eventStore = NewSystemEventStore();

        using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream       = Fixture.NewStreamName();
        var streamEvents = Fixture.CreateStreamEvents(2).ToArray();

        await eventStore.AppendEvents(stream, ExpectedStreamVersion.Any, streamEvents, cancellator.Token);

        // Act
        var readResults = await eventStore.ReadEvents(stream, StreamReadPosition.Start, 10, cancellator.Token);

        // Assert
        readResults.Length.ShouldBe(2);
    }
}