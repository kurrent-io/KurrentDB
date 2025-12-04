// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Text;
using Google.Protobuf;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.TUnit;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public class CustomIndexesServiceReadTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	CustomIndexesService.CustomIndexesServiceClient Client => KurrentContext.CustomIndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	static readonly Guid CorrelationId = Guid.NewGuid();
	static readonly string CustomIndexName = $"orders-by-country-{CorrelationId}";
	static readonly string Category = $"Orders_{CorrelationId:N}";
	static readonly string EventType = $"OrderCreated-{CorrelationId}";
	static readonly string Stream = $"{Category}-{CorrelationId}";

	ValueTask<AppendResponse> AppendEvent(string stream, string eventType, string jsonData, CancellationToken ct) {
		return StreamsWriteClient.AppendAsync(
			new() {
				ExpectedRevision = (long)ExpectedRevisionConstants.Any,
				Stream = stream,
				Records = {
					new AppendRecord() {
						RecordId = Guid.NewGuid().ToString(),
						Schema = new() {
							Name = eventType,
							Format = SchemaFormat.Json,
						},
						Data = ByteString.CopyFromUtf8(jsonData),
					}
				},
			},
			cancellationToken: ct);
	}

	[Test]
	public async ValueTask can_setup() {
		var ct = TestContext.CancellationToken;
		await AppendEvent(Stream, EventType, """{ "orderId": "A", "country": "Mauritius" }""", ct);
		await AppendEvent(Stream, EventType, """{ "orderId": "B", "country": "United Kingdom" }""", ct);
		await AppendEvent(Stream, EventType, """{ "orderId": "C", "country": "Mauritius" }""", ct);

		await Client.CreateCustomIndexAsync(new() {
			Name = CustomIndexName,
			Filter = $"e => e.type == '{EventType}'",
			PartitionKeySelector = "e => e.data.country",
			PartitionKeyType = KeyType.String,
			Enable = true,
		});
	}

	[Test]
	[DependsOn(nameof(can_setup))]
//	[Retry(5)] // because index processing is asynchronous to the write //qq but retry causes complaint from TestContextTxtensions that '$TestLoggerFactory' item already exists in the TestContext ObjectBag!
	[Arguments("", 3)]
	[Arguments("Mauritius", 2)]
	[Arguments("United Kingdom", 1)]
	[Arguments("united kingdom", 0)]
	public async ValueTask can_read_custom_index_forwards(string partition, int expectedCount) {
		var partitionSuffix = partition is "" ? "" : $":{partition}";
		var events = await StreamsReadClient
			//qq need a different pattern so that user names do not conflict with the default indexes
			.ReadAllForwardFiltered($"$idx-{CustomIndexName}{partitionSuffix}", TestContext.CancellationToken)
			.ToArrayAsync(TestContext.CancellationToken);
		var payloads = events.Select(evt => Encoding.UTF8.GetString(evt.Data.Span)).ToArray();
		await Assert.That(events.Count).IsEqualTo(expectedCount);
		await Assert.That(events).IsOrderedBy(x => x.EventNumber);
		if (partition is not "")
			await Assert.That(payloads).All(x => x.Contains($"\"country\": \"{partition}\""));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
//	[Retry(5)] // because index processing is asynchronous to the write //qq but retry causes complaint from TestContextTxtensions that '$TestLoggerFactory' item already exists in the TestContext ObjectBag!
	[Arguments("", 3)]
	[Arguments("Mauritius", 2)]
	[Arguments("United Kingdom", 1)]
	[Arguments("united kingdom", 0)]
	public async ValueTask can_read_custom_index_backwards(string partition, int expectedCount) {
		var partitionSuffix = partition is "" ? "" : $":{partition}";
		var events = await StreamsReadClient
			//qq need a different pattern so that user names do not conflict with the default indexes
			.ReadAllBackwardFiltered($"$idx-{CustomIndexName}{partitionSuffix}", TestContext.CancellationToken)
			.ToArrayAsync(TestContext.CancellationToken);
		var payloads = events.Select(evt => Encoding.UTF8.GetString(evt.Data.Span)).ToArray();
		await Assert.That(events.Count).IsEqualTo(expectedCount);
		await Assert.That(events).IsOrderedByDescending(x => x.EventNumber);
		if (partition is not "")
			await Assert.That(payloads).All(x => x.Contains($"\"country\": \"{partition}\""));
	}

	//qq write some events and see them come out of the index// - in all test check some actual data.
	// - read non-existant
	// - subscribe
	// - subscription behaviour when enable/disable/delete.
	// - test the properties available to the functions
	// - test if the js throws, returns the wrong type, etc.

	[Test]
	[DependsOn(nameof(can_setup))]
//	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_category_index_forwards() {
		var events = await StreamsReadClient
			.ReadAllForwardFiltered($"$idx-ce-{Category}", TestContext.CancellationToken)
			.ToArrayAsync(TestContext.CancellationToken);
		var payloads = events.Select(evt => Encoding.UTF8.GetString(evt.Data.Span)).ToArray();
		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedBy(x => x.Position.PreparePosition);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
//	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_category_index_backwards() {
		var events = await StreamsReadClient
			.ReadAllBackwardFiltered($"$idx-ce-{Category}", TestContext.CancellationToken)
			.ToArrayAsync(TestContext.CancellationToken);
		var payloads = events.Select(evt => Encoding.UTF8.GetString(evt.Data.Span)).ToArray();
		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedByDescending(x => x.Position.PreparePosition);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
//	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_event_type_index_forwards() {
		var events = await StreamsReadClient
			.ReadAllForwardFiltered($"$idx-et-{EventType}", TestContext.CancellationToken)
			.ToArrayAsync(TestContext.CancellationToken);
		var payloads = events.Select(evt => Encoding.UTF8.GetString(evt.Data.Span)).ToArray();
		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedBy(x => x.Position.PreparePosition);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
//	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_event_type_index_backwards() {
		var events = await StreamsReadClient
			.ReadAllBackwardFiltered($"$idx-et-{EventType}", TestContext.CancellationToken)
			.ToArrayAsync(TestContext.CancellationToken);
		var payloads = events.Select(evt => Encoding.UTF8.GetString(evt.Data.Span)).ToArray();
		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedByDescending(x => x.Position.PreparePosition);
	}
}
