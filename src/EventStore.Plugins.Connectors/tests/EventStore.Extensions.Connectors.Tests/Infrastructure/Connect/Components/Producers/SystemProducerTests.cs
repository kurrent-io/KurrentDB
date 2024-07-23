using EventStore.Connect.Consumers;
using EventStore.Core;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;

namespace EventStore.Extensions.Connectors.Tests.Connect.Producers;

[Trait("Category", "Integration")]
public class SystemProducerTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
	[Theory]
	[InlineData(1, 1)]
	[InlineData(1, 3)]
	[InlineData(3, 1)]
	[InlineData(10, 10)]
	public async Task sends_messages(int numberOfRequests, int batchSize, string? streamId = null) {
		// Arrange
		streamId ??= Fixture.NewStreamId();

		var numberOfMessages = numberOfRequests * batchSize;

		var requests = Fixture.GenerateTestSendRequests(streamId, numberOfRequests, batchSize);
		var messages = requests.SelectMany(r => r.Messages).ToList();

		await using var producer = Fixture.NewProducer()
			.ProducerId($"pdr-{streamId}-{numberOfRequests:000}-{batchSize:000}")
			.Create();

		// Act
		var results = new List<ProduceResult>();

		foreach (var request in requests)
			results.Add(await producer.Produce(request));

		// Assert
		results.Should().HaveCount(numberOfRequests, "because there should be one result for each request");

		results.All(x => x.Success).Should().BeTrue("because all the messages should be sent successfully");

		var lastPosition = results.Last().Position;

		var actualEvents = await Fixture.Publisher.ReadFullStream(streamId).ToListAsync();

		actualEvents.Should().HaveCount(numberOfMessages, "because there should be one event for each message sent");

		for (var i = 0; i < actualEvents.Count; i++) {
			var actualRecord  = await actualEvents[i].ToRecord(Fixture.SchemaSerializer.Deserialize, i + 1);
			var actualMessage = MapRecordToMessage(actualRecord);
			var sentMessage   = messages[i];

			actualMessage.Should().BeEquivalentTo(sentMessage,
				options => options.WithTracing()
                    .Excluding(x => x.Schema.Subject)
                    .Excluding(x => x.Schema.SubjectMissing)
                    .ComparingByValue(typeof(PartitionKey)),
				"because the actual message should be the same as the sent message");

			if (i == actualEvents.Count - 1)
				lastPosition.Should().BeEquivalentTo(actualRecord.Position, "because the last position should be the same as the last event's position");
		}

		return;

        static Message MapRecordToMessage(EventStoreRecord record) =>
            new() {
                Value    = record.Value,
                Key      = record.Key,
                Headers  = record.Headers,
                RecordId = record.Id,
                Schema   = record.SchemaInfo
            };
	}

	[Theory]
	[InlineData(10, 1, 10)]
	[InlineData(100, 1, 10)]
	public async Task sends_messages_in_parallel(int numberOfStreams, int numberOfRequests, int batchSize) {
		var streams = Enumerable.Range(1, numberOfStreams).Select(_ => Fixture.NewStreamId()).ToList();
		await Parallel.ForEachAsync(streams, async (streamId, _) => await sends_messages(numberOfRequests, batchSize, streamId));
	}
}