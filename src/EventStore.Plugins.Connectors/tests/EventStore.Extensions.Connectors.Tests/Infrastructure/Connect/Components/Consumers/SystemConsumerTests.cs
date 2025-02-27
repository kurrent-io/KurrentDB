// ReSharper disable MethodSupportsCancellation

using System.Text.RegularExpressions;
using EventStore.Connect.Consumers;
using EventStore.Core;
using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using EventStore.Toolkit.Testing.Xunit;

namespace EventStore.Extensions.Connectors.Tests.Connect.Consumers;

[Trait("Category", "Integration")]
public class SystemConsumerTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
	[Theory, ConsumeFilterCases]
	public async Task consumes_stream_from_earliest(string streamId, ConsumeFilter filter) {
		// Arrange
		var requests = await Fixture.ProduceTestEvents(streamId, 1, 10);
		var messages = requests.SelectMany(x => x.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(360));

		var pendingCount = messages.Count;

		var consumedRecords = new List<SurgeRecord>();

		await using var consumer = Fixture.NewConsumer()
			.ConsumerId($"{streamId}-csr")
			.Filter(filter)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.DisableAutoCommit()
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
			actualEvents.Select((re, idx) => re.ToRecord(Fixture.SchemaSerializer.Deserialize, idx + 1).AsTask()).ToArray()
		);

		consumedRecords.Should()
			.BeEquivalentTo(actualRecords, options => options.WithStrictOrderingFor(x => x.Position), "because we consumed all the records in the stream");
	}

	[Theory, ConsumeFilterCases]
    public Task consumes_stream_from_latest(string streamId, ConsumeFilter filter) => Fixture.TestWithTimeout(
        async cancellator => {
            // Arrange
            await Fixture.ProduceTestEvents(streamId);

            await using var consumer = Fixture.NewConsumer()
                .ConsumerId($"{streamId}-csr")
                .Filter(filter)
                .InitialPosition(SubscriptionInitialPosition.Latest)
                .DisableAutoCommit()
                .Create();

            // Act
            var consumed = await consumer
                .Records(cancellator.Token)
                .ToListAsync(cancellator.Token);

            // Assert
            consumed.Should().BeEmpty("because there are no records in the stream");
        }
    );

	[Theory, ConsumeFilterCases]
	public async Task consumes_stream_from_start_position(string streamId, ConsumeFilter filter) {
		// Arrange
        var noise         = await Fixture.ProduceTestEvents(streamId);
        var startPosition = noise.Single().Position;
        var requests      = await Fixture.ProduceTestEvents(streamId);
        var messages      = requests.SelectMany(x => x.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromMinutes(1));

		var consumedRecords = new List<SurgeRecord>();

		await using var consumer = Fixture.NewConsumer()
			.ConsumerId($"{streamId}-csr")
			.Filter(filter)
			.StartPosition(startPosition)
			.DisableAutoCommit()
			.Create();

		// Act
		await foreach (var record in consumer.Records(cancellator.Token)) {
			messages.Should().Contain(x => x.RecordId == record.Id);

			consumedRecords.Add(record);
			await consumer.Track(record);

			if (consumedRecords.Count == messages.Count)
				await cancellator.CancelAsync();
		}

		await consumer.CommitAll();

		// Assert
		consumedRecords.All(x => x.StreamId == streamId).Should().BeTrue();
		consumedRecords.Should().HaveCount(messages.Count);

		var positions = await consumer.GetLatestPositions();

		positions.Last().Should().BeEquivalentTo(consumedRecords.Last().Position);
	}

	async Task<RecordPosition> ProduceAndConsumeTestStream(
        string streamId, ConsumeFilter filter, int numberOfMessages, CancellationToken cancellationToken, bool commit = true
    ) {
		using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var requests        = await Fixture.ProduceTestEvents(streamId, numberOfRequests: 1, numberOfMessages);
        var messageCount    = requests.SelectMany(x => x.Messages).Count();
        var consumedRecords = new List<SurgeRecord>();

		await using var consumer = Fixture.NewConsumer()
			.ConsumerId($"{streamId}-csr")
			.SubscriptionName($"{streamId}-csr")
			.Filter(filter)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.DisableAutoCommit()
			.Create();

		await foreach (var record in consumer.Records(cancellator.Token)) {
			consumedRecords.Add(record);
			await consumer.Track(record);

			if (consumedRecords.Count == messageCount)
				await cancellator.CancelAsync();
		}

        if (commit)
		    await consumer.CommitAll();

		var latestPositions = await consumer.GetLatestPositions(CancellationToken.None);

		return latestPositions.LastOrDefault();

		// return consumedRecords.Last().Position;
	}

	[Theory, ConsumeFilterCases]
	public async Task consumes_stream_from_last_committed_position(string streamId, ConsumeFilter filter) {
		// Arrange
		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(720));

		var latestRecordPosition = await ProduceAndConsumeTestStream(streamId, filter, 10, cancellator.Token, commit: false);

		var requests        = await Fixture.ProduceTestEvents(streamId, 1, 1);
		var messages        = requests.SelectMany(x => x.Messages).ToList();
		var consumedRecords = new List<SurgeRecord>();

		await using var consumer = Fixture.NewConsumer()
			.ConsumerId($"{streamId}-csr")
			.SubscriptionName($"{streamId}-csr")
			.Filter(filter)
            .StartPosition(latestRecordPosition.LogPosition)
            .DisableAutoCommit()
			.Create();

		// Act
		await foreach (var record in consumer.Records(cancellator.Token)) {
			consumedRecords.Add(record);
			await consumer.Track(record);

			if (consumedRecords.Count == messages.Count)
				await cancellator.CancelAsync();
		}

		await consumer.CommitAll();

		// Assert
		consumedRecords.All(x => x.StreamId == streamId).Should().BeTrue();
		consumedRecords.Should().HaveCount(messages.Count);
	}

	[Theory, ConsumeFilterCases]
	public async Task consumes_stream_and_commits_positions_on_dispose(string streamId, ConsumeFilter filter) {
		// Arrange
		var requests = await Fixture.ProduceTestEvents(streamId, 1, 10);
		var messages = requests.SelectMany(x => x.Messages).ToList();

		using var cancellator = new CancellationTokenSource(TimeSpan.FromSeconds(360));

		var consumedRecords = new List<SurgeRecord>();

		var consumer = Fixture.NewConsumer()
			.ConsumerId($"{streamId}-csr")
			.Filter(filter)
            .InitialPosition(SubscriptionInitialPosition.Earliest)
			.AutoCommit(x => x with { RecordsThreshold = 1 })
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

    class ConsumeFilterCases : TestCaseGenerator<ConsumeFilterCases> {
        protected override IEnumerable<object[]> Data() {
            var streamId = Guid.NewGuid().ToString();
            yield return [streamId, ConsumeFilter.FromStreamId(streamId)];

            streamId = Guid.NewGuid().ToString();
            yield return [streamId, ConsumeFilter.FromRegex(ConsumeFilterScope.Stream, new Regex(streamId))];
        }
    }
}
