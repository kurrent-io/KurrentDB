// ReSharper disable MethodSupportsCancellation

using EventStore.Core;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;

namespace EventStore.Extensions.Connectors.Tests.Streaming.Consumers;

[Trait("Category", "Integration")]
public class SystemConsumerTests(ITestOutputHelper output, StreamingFixture fixture) : StreamingTests(output, fixture) {
	[Fact]
	public async Task consumes_stream_from_earliest() {
		// Arrange
		var streamId = Fixture.NewStreamId();

		var requests = await Fixture.ProduceTestEvents(streamId, 1, 10);
		var messages = requests.SelectMany(x => x.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(360));

		var pendingCount = messages.Count;

		var consumedRecords = new List<EventStoreRecord>();

		await using var consumer = SystemConsumer.Builder
			.ConsumerId($"{streamId}-csr")
			.Streams(streamId)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.AutoCommit(x => x with { AutoCommitEnabled = false })
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Create();

		// Act
		await foreach (var record in consumer.Records(cancellator.Token)) {
			pendingCount--;
			consumedRecords.Add(record);

			if (pendingCount == 0)
				await cancellator.CancelAsync();
		}

		// Assert
		consumedRecords.Should()
			.HaveCount(messages.Count, "because we consumed all the records in the stream");

		var actualEvents = await Fixture.Publisher.ReadFullStream(streamId).ToListAsync();

		var actualRecords = await Task.WhenAll(
			actualEvents.Select((re, idx) => re.ToRecord(Fixture.SchemaSerializer.Deserialize, idx).AsTask()).ToArray()
		);

		consumedRecords.Should()
			.BeEquivalentTo(actualRecords, options => options.WithStrictOrderingFor(x => x.Position), "because we consumed all the records in the stream");
	}

	[Fact]
	public async Task consumes_stream_from_latest() {
		// Arrange
		var streamId = Fixture.NewStreamId();

		var requests = await Fixture.ProduceTestEvents(streamId);
		var messages = requests.SelectMany(x => x.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		await using var consumer = SystemConsumer.Builder
			.ConsumerId($"{streamId}-csr")
			.Streams(streamId)
			.InitialPosition(SubscriptionInitialPosition.Latest)
			.DisableAutoCommit()
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Create();

		// Act
		var consumed = await consumer
			.Records(cancellator.Token)
			.ToListAsync(cancellator.Token);

		// Assert
		consumed.Should().BeEmpty("because there are no records in the stream");
	}

	[Fact]
	public async Task consumes_stream_from_start_position() {
		// Arrange
		var streamId      = Fixture.NewStreamId();
		var noise         = await Fixture.ProduceTestEvents(streamId);
		var startPosition = noise.Single().Position;
		var requests      = await Fixture.ProduceTestEvents(streamId);
		var messages      = requests.SelectMany(x => x.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromMinutes(1));

		var consumedRecords = new List<EventStoreRecord>();

		await using var consumer = SystemConsumer.Builder
			.ConsumerId($"{streamId}-csr")
			.Streams(streamId)
			.StartPosition(startPosition)
			.DisableAutoCommit()
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Create();

		// Act
		await foreach (var record in consumer.Records(cancellator.Token)) {
			messages.Should().Contain(x => x.RecordId == record.Id);

			consumedRecords.Add(record);
			await consumer.Track(record);

			if (consumedRecords.Count == messages.Count)
				await cancellator.CancelAsync();
		}

		await consumer.Commit();

		// Assert
		consumedRecords.All(x => x.StreamId == streamId).Should().BeTrue();
		consumedRecords.Should().HaveCount(messages.Count);

		var positions = await consumer.GetLatestPositions();

		positions.Last().Should().BeEquivalentTo(consumedRecords.Last().Position);
	}

	async Task<RecordPosition> ProduceAndConsumeTestStream(string streamId, int numberOfMessages, CancellationToken cancellationToken) {
		using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		var requests        = await Fixture.ProduceTestEvents(streamId, numberOfRequests: 1, numberOfMessages);
		var messageCount    = requests.SelectMany(x => x.Messages).Count();
		var consumedRecords = new List<EventStoreRecord>();

		await using var consumer = SystemConsumer.Builder
			.ConsumerId($"{streamId}-csr")
			.SubscriptionName($"{streamId}-csr")
			.Streams(streamId)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.DisableAutoCommit()
			.Publisher(Fixture.Publisher)
			.Create();

		await foreach (var record in consumer.Records(cancellator.Token)) {
			consumedRecords.Add(record);
			await consumer.Track(record);

			if (consumedRecords.Count == messageCount)
				await cancellator.CancelAsync();
		}

		await consumer.Commit();

		var latestPositions = await consumer.GetLatestPositions(CancellationToken.None);

		return latestPositions.LastOrDefault();

		// return consumedRecords.Last().Position;
	}

	[Fact]
	public async Task consumes_stream_from_last_committed_position() {
		// Arrange
		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(720));

		var streamId = Fixture.NewStreamId();

		await ProduceAndConsumeTestStream(streamId, 10, cancellator.Token);

		var requests        = await Fixture.ProduceTestEvents(streamId, 1, 1);
		var messages        = requests.SelectMany(x => x.Messages).ToList();
		var consumedRecords = new List<EventStoreRecord>();

		await using var consumer = SystemConsumer.Builder
			.ConsumerId($"{streamId}-csr")
			.SubscriptionName($"{streamId}-csr")
			.Streams(streamId)
			.DisableAutoCommit()
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Create();

		// Act
		await foreach (var record in consumer.Records(cancellator.Token)) {
			consumedRecords.Add(record);
			await consumer.Track(record);

			if (consumedRecords.Count == messages.Count)
				await cancellator.CancelAsync();
		}

		await consumer.Commit();

		// Assert
		consumedRecords.All(x => x.StreamId == streamId).Should().BeTrue();
		consumedRecords.Should().HaveCount(messages.Count);
	}

	[Fact]
	public async Task consumes_stream_and_commits_positions_on_dispose() {
		// Arrange
		var streamId = Fixture.NewStreamId();

		var requests = await Fixture.ProduceTestEvents(streamId, 1, 10);
		var messages = requests.SelectMany(x => x.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(360));

		var consumedRecords = new List<EventStoreRecord>();

		var consumer = SystemConsumer.Builder
			.ConsumerId($"{streamId}-csr")
			.Streams(streamId)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.AutoCommit(x => x with { RecordsThreshold = 1 })
			.Publisher(Fixture.Publisher)
			.LoggerFactory(Fixture.LoggerFactory)
			.EnableLogging()
			.Create();

		// Act
		await foreach (var record in consumer.Records(cancellator.Token)) {
			consumedRecords.Add(record);
			await consumer.Track(record);

			if (consumedRecords.Count == messages.Count)
				await cancellator.CancelAsync();
		}

		// Assert
		consumedRecords.Should()
			.HaveCount(messages.Count, "because we consumed all the records in the stream");

		await consumer.DisposeAsync();

		var latestPositions = await consumer.GetLatestPositions(CancellationToken.None);

		latestPositions.LastOrDefault().Should()
			.BeEquivalentTo(consumedRecords.LastOrDefault().Position);
	}
}