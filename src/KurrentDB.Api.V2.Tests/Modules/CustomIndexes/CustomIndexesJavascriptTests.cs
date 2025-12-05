// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.TUnit;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public class CustomIndexesJavascriptTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	CustomIndexesService.CustomIndexesServiceClient Client => KurrentContext.CustomIndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	readonly Guid _correlationId = Guid.NewGuid();
	string CustomIndexName => $"orders-by-country-{_correlationId}";
	string Category => $"Orders_{_correlationId:N}";
	string EventType => $"OrderCreated-{_correlationId}";
	string Stream => $"{Category}-{_correlationId}";
	string ReadFilter => $"$idx-{CustomIndexName}";

	[Test]
	[Arguments(KeyType.String, """ "blue" """, """ "red" """, "red")]
	[Arguments(KeyType.String, """ "blue:2" """, """ "red:3" """, "red:3")]

	[Arguments(KeyType.Int16,         """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(KeyType.Int32,         """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(KeyType.Int64,         """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(KeyType.UnsignedInt32, """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(KeyType.UnsignedInt64, """ 0.0 """, """ 1.0 """, "1")]

	[Arguments(KeyType.Number,        """ 0   """, """ 1   """, "1")]
	[Arguments(KeyType.Number,        """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(KeyType.Number,        """ 1234.56 """, """ 6543.21 """, "6543.21")]
	//[Arguments(KeyType.Number,        """ 1234.56 """, """ 6543.21 """, "6543.210")] //qq why doesn't this work
	public async ValueTask can_partition_by_all_key_types(KeyType keyType, string partition1, string partition2, string partitionFilter) {
		var ct = TestContext.CancellationToken;

		await Client.CreateCustomIndexAsync(
			new() {
				Name = CustomIndexName,
				Filter = $"e => e.type == '{EventType}'",
				PartitionKeySelector = "e => e.data.theKey",
				PartitionKeyType = keyType,
			},
			cancellationToken: ct);

		// write an event for one partition
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A", "theKey": {{partition1}} }""", ct);
		// write an event for another partition
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "B", "theKey": {{partition2}} }""", ct);

		// ensure both events are processed by the custom index
		var evts = await StreamsReadClient.WaitForCustomIndexEvents(ReadFilter, 2, ct);
		await Assert.That(evts.Count).IsEqualTo(2);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "A", """);
		await Assert.That(evts[1].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);

		// ensure the target partition only contains the one event
		await StreamsReadClient.WaitForCustomIndexEvents($"{ReadFilter}:{partitionFilter}", 1, ct);
		evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}:{partitionFilter}", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);
	}

	[Test]
	public async ValueTask can_partition_by_unspecified_key_type() {
		var ct = TestContext.CancellationToken;

		await Client.CreateCustomIndexAsync(
			new() {
				Name = CustomIndexName,
				Filter = $"e => e.type == '{EventType}'",
				PartitionKeySelector = "e => null", //qq todo: when the partition type is unspecified we probably shouldn't call the selector
				PartitionKeyType = KeyType.Unspecified,
			},
			cancellationToken: ct);

		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A" }""", ct);

		var evts = await StreamsReadClient.WaitForCustomIndexEvents(ReadFilter, 1, ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "A" """);
	}
}
