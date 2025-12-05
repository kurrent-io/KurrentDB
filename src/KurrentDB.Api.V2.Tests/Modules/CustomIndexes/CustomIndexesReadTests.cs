// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.TUnit;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public class CustomIndexesReadTests {
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
	//qq need a different pattern so that user names do not conflict with the default indexes
	static readonly string ReadFilter = $"$idx-{CustomIndexName}";
	static readonly string CategoryFilter = $"$idx-ce-{Category}";
	static readonly string EventTypeFilter = $"$idx-et-{EventType}";


	[Test]
	public async ValueTask can_setup() {
		var ct = TestContext.CancellationToken;
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "A", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "B", "country": "United Kingdom" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "C", "country": "Mauritius" }""", ct);

		await Client.CreateCustomIndexAsync(
			new() {
				Name = CustomIndexName,
				Filter = $"e => e.type == '{EventType}'",
				PartitionKeySelector = "e => e.data.country",
				PartitionKeyType = KeyType.String,
			},
			cancellationToken: ct);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(5)] // because index processing is asynchronous to the write
	[Arguments("", 3)]
	[Arguments("Mauritius", 2)]
	[Arguments("United Kingdom", 1)]
	[Arguments("united kingdom", 0)]
	public async ValueTask can_read_custom_index_forwards(string partition, int expectedCount) {
		var ct = TestContext.CancellationToken;

		var partitionSuffix = partition is "" ? "" : $":{partition}";
		var events = await StreamsReadClient
			.ReadAllForwardFiltered($"{ReadFilter}{partitionSuffix}", ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(expectedCount);
		await Assert.That(events).IsOrderedBy(x => x.EventNumber);
		if (partition is not "")
			await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains($""" "country": "{partition}" """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(5)] // because index processing is asynchronous to the write
	[Arguments("", 3)]
	[Arguments("Mauritius", 2)]
	[Arguments("United Kingdom", 1)]
	[Arguments("united kingdom", 0)]
	public async ValueTask can_read_custom_index_backwards(string partition, int expectedCount) {
		var partitionSuffix = partition is "" ? "" : $":{partition}";
		var events = await StreamsReadClient
			.ReadAllBackwardFiltered($"{ReadFilter}{partitionSuffix}", TestContext.CancellationToken)
			.ToArrayAsync(TestContext.CancellationToken);

		await Assert.That(events.Count).IsEqualTo(expectedCount);
		await Assert.That(events).IsOrderedByDescending(x => x.EventNumber);
		if (partition is not "")
			await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains($""" "country": "{partition}" """));
	}

	//qq
	// - test the properties available to the functions
	// - test if the js throws, returns the wrong type, etc.
	// - subscribe from a position
	// - range partitions?

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_category_index_forwards() {
		var ct = TestContext.CancellationToken;

		var events = await StreamsReadClient
			.ReadAllForwardFiltered(CategoryFilter, ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedBy(x => x.Position.PreparePosition);
		await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains(""" "orderId": """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_category_index_backwards() {
		var ct = TestContext.CancellationToken;

		var events = await StreamsReadClient
			.ReadAllBackwardFiltered(CategoryFilter, ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedByDescending(x => x.Position.PreparePosition);
		await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains(""" "orderId": """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_event_type_index_forwards() {
		var ct = TestContext.CancellationToken;

		var events = await StreamsReadClient
			.ReadAllForwardFiltered(EventTypeFilter, ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedBy(x => x.Position.PreparePosition);
		await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains(""" "orderId": """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(5)] // because index processing is asynchronous to the write
	public async ValueTask can_read_event_type_index_backwards() {
		var ct = TestContext.CancellationToken;

		var events = await StreamsReadClient
			.ReadAllBackwardFiltered(EventTypeFilter, ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedByDescending(x => x.Position.PreparePosition);
		await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains(""" "orderId": """));
	}

	[Test]
	[Arguments("")]
	[Arguments("Mauritius")]
	[Arguments("United Kingdom")]
	[Arguments("united kingdom")]
	public async ValueTask cannot_read_non_existent_custom_index(string partition) {
		var partitionSuffix = partition is "" ? "" : $":{partition}";
		var index = $"$idx-does-not-exist{partitionSuffix}";
		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					//qq need a different pattern so that user names do not conflict with the default indexes
					.ReadAllForwardFiltered(index, TestContext.CancellationToken)
					.ToArrayAsync(TestContext.CancellationToken);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{index}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}
}
