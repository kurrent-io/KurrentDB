// ReSharper disable AccessToDisposedClosure
// ReSharper disable MethodSupportsCancellation

using System.Text.RegularExpressions;
using EventStore.Connect.Consumers;
using EventStore.Core;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Toolkit.Testing;
using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Consumers.Checkpoints;
using Kurrent.Surge.Processors;
using Microsoft.Extensions.Logging;

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

            var processedRecords = new List<SurgeRecord>();

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

    [Theory]
    [InlineData(100)]
    public Task handles_checkpoint_received_and_caught_up_events(int numberOfMessages) => Fixture.TestWithTimeout(
        TimeSpan.FromSeconds(60),
        async cancellator => {
            // Arrange
            await Fixture.ProduceTestEvents(Identifiers.GenerateShortId("stream"), 1, numberOfMessages);

            var processorId = $"{Identifiers.GenerateShortId()}-prx" ;

            var processedRecords = new List<SurgeRecord>();

            var processor = Fixture.NewProcessor()
                .ProcessorId(processorId)
                // .Filter(ConsumeFilter.FromRegex(ConsumeFilterScope.Record, new Regex(Identifiers.GenerateShortId())))
                .Filter(ConsumeFilter.ExcludeSystemEvents())
                .InitialPosition(SubscriptionInitialPosition.Earliest)
                .AutoCommit(options => options with { RecordsThreshold = 10, Interval = TimeSpan.FromDays(1)}) // this will be the default value for the max search window
                .Process(async ctx => {
                    if (ctx.Record.Value is ReadResponse.CheckpointReceived) {
                        processedRecords.Add(ctx.Record);
                        Fixture.Logger.LogWarning("Checkpoint received: {Position}", ctx.Record.Position);
                    }
                    else if (ctx.Record.Value is ReadResponse.SubscriptionCaughtUp) {
                        await cancellator.CancelAsync();
                        Fixture.Logger.LogWarning("Subscription caught up: {Position}", ctx.Record.Position);
                    }
                })
                .Create();

            // Act
            await processor.RunUntilDeactivated(cancellator.Token);

            // Assert
            Fixture.Logger.LogInformation("Received {Count} system events", processedRecords.Count);

            processedRecords.Should()
                .HaveCountGreaterOrEqualTo(10, "because we should have received at least 10 CheckpointReceived events");

            var checkpointStore = new CheckpointStore(
                processorId,
                Fixture.Producer,
                Fixture.Reader,
                TimeProvider.System,
                $"$consumers/{processorId}/checkpoints"
            );

            var positions = await checkpointStore.LoadPositions();

            positions.Should()
                .HaveCountGreaterOrEqualTo(1, "because we should have received at least 1 CheckpointReceived event");

            Fixture.Logger.LogInformation("#### Checkpoint position: {Position}", positions[^1]);

            positions[^1].LogPosition.CommitPosition.Should().Be(
                processedRecords[^1].Position.LogPosition.CommitPosition,
                "because the last checkpoint position should be the same as the last CheckpointReceived event position"
            );
        }
    );
}