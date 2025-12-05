// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Grpc.Core;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.TUnit;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public static class SpanOfByteExtensions {
	public static string ToStringUtf8(this ReadOnlySpan<byte> self) => Encoding.UTF8.GetString(self);
	public static string ToStringUtf8(this ReadOnlyMemory<byte> self) => self.Span.ToStringUtf8();
}

public static class AsyncEnumeratorExtensions {
	public static async ValueTask<T> ConsumeNext<T>(this IAsyncEnumerator<T> self) {
		if (!await self.MoveNextAsync())
			throw new InvalidOperationException("end of sequence reached");
		return self.Current;
	}
}

public class CustomIndexesSubscriptionTests {
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

	[Test]
	public async ValueTask can_subscribe() {
		var ct = TestContext.CancellationToken;

		// events that existed before index
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "A", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "B", "country": "United Kingdom" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "C", "country": "Mauritius" }""", ct);

		// create index
		await Client.CreateCustomIndexAsync(
			new() {
				Name = CustomIndexName,
				Filter = $"e => e.type == '{EventType}'",
				PartitionKeySelector = "e => e.data.country",
				PartitionKeyType = KeyType.String,
			},
			cancellationToken: ct);

		var allPartitions = $"$idx-{CustomIndexName}";
		var mauritiusPartition = $"{allPartitions}:Mauritius";

		// wait for index to become available
		await StreamsReadClient.WaitForCustomIndexEvents(allPartitions, 1, ct);
		await StreamsReadClient.WaitForCustomIndexEvents(mauritiusPartition, 1, ct);

		// subscribe
		await using var allPartitionsEnumerator = StreamsReadClient.SubscribeToAllFiltered(allPartitions, ct).GetAsyncEnumerator(ct);
		await using var mauritiusEnumerator = StreamsReadClient.SubscribeToAllFiltered(mauritiusPartition, ct).GetAsyncEnumerator(ct);

		await Assert.That((await allPartitionsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "A", """);
		await Assert.That((await allPartitionsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "B", """);
		await Assert.That((await allPartitionsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "C", """);

		await Assert.That((await mauritiusEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "A", """);
		await Assert.That((await mauritiusEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "C", """);

		// write more and receive
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "D", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "E", "country": "United Kingdom" }""", ct);

		await Assert.That((await allPartitionsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "D", """);
		await Assert.That((await allPartitionsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "E", """);

		await Assert.That((await mauritiusEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "D", """);

		// disable
		await Client.DisableCustomIndexAsync(new() { Name = CustomIndexName }, cancellationToken: ct);

		await Task.Delay(500); // todo better way to wait for it to disable

		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "F", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "G", "country": "United Kingdom" }""", ct);

		var nextAllResult = allPartitionsEnumerator.ConsumeNext();
		var nextMauritiusResult = mauritiusEnumerator.ConsumeNext();
		await Task.Delay(500);

		await Assert.That(nextAllResult.IsCompleted).IsFalse();
		await Assert.That(nextMauritiusResult.IsCompleted).IsFalse();

		// enable and receive the extra events
		await Client.EnableCustomIndexAsync(new() { Name = CustomIndexName }, cancellationToken: ct);

		await Assert.That((await nextAllResult).Data.ToStringUtf8()).Contains(""" "orderId": "F", """);
		await Assert.That((await allPartitionsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "G", """);

		await Assert.That((await nextMauritiusResult).Data.ToStringUtf8()).Contains(""" "orderId": "F", """);

		// delete
		await Client.DeleteCustomIndexAsync(new() { Name = CustomIndexName }, cancellationToken: ct);

		var ex = await Assert
			.That(async () => await allPartitionsEnumerator.ConsumeNext())
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{allPartitions}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);

		ex = await Assert
			.That(async () => await mauritiusEnumerator.ConsumeNext())
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{mauritiusPartition}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	[Arguments("")]
	[Arguments("Mauritius")]
	public async ValueTask cannot_subscribe_to_non_existent_custom_index(string partition) {
		var partitionSuffix = partition is "" ? "" : $":{partition}";
		var index = $"$idx-does-not-exist{partitionSuffix}";
		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					.SubscribeToAllFiltered(index, TestContext.CancellationToken)
					.ToArrayAsync(TestContext.CancellationToken);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{index}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}
}
