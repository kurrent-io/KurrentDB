// ReSharper disable AccessToDisposedClosure
// ReSharper disable MethodSupportsCancellation

using EventStore.Core;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Processors;

namespace EventStore.Plugins.Connectors.Tests.Streaming.Processors;

[Trait("Category", "Integration")]
public class SystemProcessorTests(ITestOutputHelper output, StreamingFixture fixture) : StreamingTests(output, fixture) {
	[Theory]
	[InlineData(1)]
	[InlineData(100)]
	public async Task processes_records_from_earliest(int numberOfMessages) {
		// Arrange
		var streamId = Fixture.NewStreamId();

		var requests = await Fixture.ProduceTestEvents(streamId, 1, numberOfMessages);
		var messages = requests.SelectMany(r => r.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromMinutes(1));

		var processedRecords = new List<EventStoreRecord>();

		var processor = SystemProcessor.Builder
			.ProcessorId($"{streamId}-prx")
			.Streams(streamId)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.DisableAutoCommit()
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Process<TestEvent>(
				async (_, ctx) => {
					processedRecords.Add(ctx.Record);

					if (processedRecords.Count == messages.Count)
						await cancellator.CancelAsync();

				})
			.Create();

		// Act
		await processor.RunUntilDeactivated(cancellator.Token);

		// Assert
		processedRecords.Should()
			.HaveCount(numberOfMessages, "because there should be one record for each message sent");

		var actualEvents = await Fixture.Publisher.ReadFullStream(streamId).ToListAsync();

		var actualRecords = await Task.WhenAll(actualEvents.Select((re, idx) => re.ToRecord(Fixture.SchemaSerializer.Deserialize, idx).AsTask()));

		processedRecords.Should()
			.BeEquivalentTo(actualRecords, "because the processed records should be the same as the actual records");
	}

	[Theory]
	[InlineData(1)]
	[InlineData(100)]
	public async Task processes_records_and_produces_output(int numberOfMessages) {
		// Arrange
        StreamId inputStreamId  = Fixture.NewStreamId();
        StreamId outputStreamId = Fixture.NewStreamId();

		var requests = await Fixture.ProduceTestEvents(inputStreamId, 1, numberOfMessages);
		var messages = requests.SelectMany(r => r.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromMinutes(1));

		var messagesSent = new List<TestEvent>();

		var processor = SystemProcessor.Builder
			.ProcessorId($"{inputStreamId}-prx")
			.Streams(inputStreamId)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.AutoCommit(x => x with { RecordsThreshold = 1 })
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Process<TestEvent>(
				async (_, ctx) => {
					var outputMessage = new TestEvent(Guid.NewGuid(), messagesSent.Count);

					ctx.Output(outputMessage, outputStreamId);

					messagesSent.Add(outputMessage);

					if (messagesSent.Count == messages.Count)
						await cancellator.CancelAsync();

				})
			.Create();

		// Act
		await processor.RunUntilDeactivated(cancellator.Token);

		// Assert
		var producedEvents = await Fixture.Publisher.ReadFullStream(outputStreamId).ToListAsync();

		producedEvents.Should()
			.HaveCount(messagesSent.Count, "because there should be one event for each message sent");
	}

	[Fact]
	public async Task stops_on_user_exception() {
		// Arrange
		var streamId = Fixture.NewStreamId();

		await Fixture.ProduceTestEvents(streamId, 1, 1);

		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		var processor = SystemProcessor.Builder
			.ProcessorId($"{streamId}-prx")
			.Streams(streamId)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.DisableAutoCommit()
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Process<TestEvent>((_, _) => throw new ApplicationException("BOOM!"))
			.Create();

		// Act & Assert
		var operation = async () => await processor.RunUntilDeactivated(cancellator.Token);

		await operation.Should()
			.ThrowAsync<ApplicationException>("because the processor should stop on exception");
	}

	[Fact]
	public async Task stops_on_dispose() {
		// Arrange
		var streamId = Fixture.NewStreamId();

		await Fixture.ProduceTestEvents(streamId, 1, 1);

		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		var processor = SystemProcessor.Builder
			.ProcessorId($"{streamId}-prx")
			.Streams(streamId)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.DisableAutoCommit()
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Process<TestEvent>((_, _) => Task.Delay(TimeSpan.MaxValue))
			.Create();

		// Act & Assert
		await processor.Activate(cancellator.Token);

		var operation = async () => await processor.DisposeAsync();

		await operation.Should()
			.NotThrowAsync("because the processor should stop on dispose");
	}
}