// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using Kurrent.Surge.Producers;
using KurrentDB.Connect.Consumers;
using KurrentDB.Surge.Testing.Fixtures;

namespace KurrentDB.Surge.Tests.Components.Producers;

[Trait("Category", "Integration")]
public class SystemProducerTests(ITestOutputHelper output, SystemComponentsAssemblyFixture fixture) : SystemComponentsIntegrationTests(output, fixture) {
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

		var actualEvents = await Fixture.Client.Reading.ReadFullStream(streamId).ToListAsync();

		actualEvents.Should().HaveCount(numberOfMessages, "because there should be one event for each message sent");

		for (var i = 0; i < actualEvents.Count; i++) {
			var actualRecord  = await actualEvents[i].ToRecord((data, headers) => Fixture.SchemaSerializer.Deserialize(data, new(headers)), i + 1);
			var actualMessage = MapRecordToMessage(actualRecord);
			var sentMessage   = messages[i];

			actualMessage.Should().BeEquivalentTo(sentMessage,
				options => options.WithTracing()
                    .Excluding(x => x.Schema.SchemaName)
                    .Excluding(x => x.Schema.SchemaNameMissing)
                    .ComparingByValue(typeof(PartitionKey)),
				"because the actual message should be the same as the sent message");

			if (i == actualEvents.Count - 1)
				lastPosition.Should().BeEquivalentTo(actualRecord.Position, "because the last position should be the same as the last event's position");
		}

		return;

        static Message MapRecordToMessage(SurgeRecord record) =>
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

	[Theory]
	[InlineData(2, 1)]
	[InlineData(3, 1)]
	[InlineData(2, 5)]
	[InlineData(5, 3)]
	public async Task produces_to_multiple_streams(int numberOfStreams, int batchSize) {
		// Arrange
		var streamIds = Enumerable.Range(1, numberOfStreams).Select(_ => Fixture.NewStreamId()).ToList();

		var requests = streamIds
			.Select(streamId => Fixture.GenerateTestProduceRequest(streamId, batchSize))
			.ToArray();

		await using var producer = Fixture.NewProducer()
			.ProducerId($"pdr-multiple-{numberOfStreams}-{batchSize}")
			.Create();

		ProduceResult? result = null;

		// Act
		await producer.Produce(requests, r => {
			result = r;
			return Task.CompletedTask;
		});

		// Assert
		result.Should().NotBeNull();

		for (var streamIndex = 0; streamIndex < streamIds.Count; streamIndex++) {
			var streamId = streamIds[streamIndex];
			var request = requests[streamIndex];

			var actualEvents = await Fixture.Client.Reading.ReadFullStream(streamId).ToListAsync();

			actualEvents.Should().HaveCount(batchSize);

			for (var i = 0; i < actualEvents.Count; i++) {
				var actualRecord = await actualEvents[i].ToRecord((data, headers) => Fixture.SchemaSerializer.Deserialize(data, new(headers)), i + 1);
				var actualMessage = MapRecordToMessage(actualRecord);
				var sentMessage = request.Messages[i];

				actualMessage.Should().BeEquivalentTo(sentMessage,
					options => options.WithTracing()
						.Excluding(x => x.Schema.SchemaName)
						.Excluding(x => x.Schema.SchemaNameMissing)
						.ComparingByValue(typeof(PartitionKey)));
			}
		}

		return;

		static Message MapRecordToMessage(SurgeRecord record) =>
			new() {
				Value    = record.Value,
				Key      = record.Key,
				Headers  = record.Headers,
				RecordId = record.Id,
				Schema   = record.SchemaInfo
			};
	}
}
