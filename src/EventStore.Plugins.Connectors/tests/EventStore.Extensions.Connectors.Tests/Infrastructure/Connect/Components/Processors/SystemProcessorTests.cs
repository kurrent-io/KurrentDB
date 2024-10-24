// ReSharper disable AccessToDisposedClosure
// ReSharper disable MethodSupportsCancellation

using EventStore.Connect.Consumers;
using EventStore.Core;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Processors;

namespace EventStore.Extensions.Connectors.Tests.Connect.Processors;

[Trait("Category", "Integration")]
public class SystemProcessorTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public Task processes_records_from_earliest(int numberOfMessages) => Fixture.TestWithTimeout(
        TimeSpan.FromSeconds(30),
        async cancellator => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            var requests = await Fixture.ProduceTestEvents(streamId, 1, numberOfMessages);
            var messages = requests.SelectMany(r => r.Messages).ToList();

            var processedRecords = new List<EventStoreRecord>();

            var processor = Fixture.NewProcessor()
                .ProcessorId($"{streamId}-prx")
                .Stream(streamId)
                .InitialPosition(SubscriptionInitialPosition.Earliest)
                .DisableAutoCommit()
                .Process<TestEvent>(
                    async (_, ctx) => {
                        processedRecords.Add(ctx.Record);

                        if (processedRecords.Count == messages.Count)
                            await cancellator.CancelAsync();
                    }
                )
                .Create();

            // Act
            await processor.RunUntilDeactivated(cancellator.Token);

            // Assert
            processedRecords.Should()
                .HaveCount(numberOfMessages, "because there should be one record for each message sent");

            var actualEvents = await Fixture.Publisher.ReadFullStream(streamId).ToListAsync();

            var actualRecords = await Task.WhenAll(actualEvents.Select((re, idx) => re.ToRecord(Fixture.SchemaSerializer.Deserialize, idx + 1).AsTask()));

            processedRecords.Should()
                .BeEquivalentTo(actualRecords, "because the processed records should be the same as the actual records");
        }
    );

    [Fact]
    public Task stops_on_user_exception() => Fixture.TestWithTimeout(
        TimeSpan.FromSeconds(30),
        async cancellator => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            await Fixture.ProduceTestEvents(streamId, 1, 1);

            var processor = Fixture.NewProcessor()
                .ProcessorId($"{streamId}-prx")
                .Stream(streamId)
                .InitialPosition(SubscriptionInitialPosition.Earliest)
                .DisableAutoCommit()
                .Process<TestEvent>((_, _) => throw new ApplicationException("BOOM!"))
                .Create();

            // Act & Assert
            var operation = async () => await processor.RunUntilDeactivated(cancellator.Token);

            await operation.Should()
                .ThrowAsync<ApplicationException>("because the processor should stop on exception");
        }
    );

    [Fact]
    public Task stops_on_dispose() => Fixture.TestWithTimeout(
        TimeSpan.FromSeconds(30),
        async cancellator => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            await Fixture.ProduceTestEvents(streamId, 1, 1);

            var processor = Fixture.NewProcessor()
                .ProcessorId($"{streamId}-prx")
                .Stream(streamId)
                .InitialPosition(SubscriptionInitialPosition.Earliest)
                .DisableAutoCommit()
                .Process<TestEvent>((_, _) => Task.Delay(TimeSpan.MaxValue))
                .Create();

            // Act & Assert
            await processor.Activate(cancellator.Token);

            var operation = async () => await processor.DisposeAsync();

            await operation.Should()
                .NotThrowAsync("because the processor should stop on dispose");
        }
    );
}