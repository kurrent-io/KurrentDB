// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using DotNext.Collections.Generic;
using Grpc.Core;
using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.Indexes;

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

public class IndexesSubscriptionTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient IndexesClient => KurrentContext.IndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	static readonly Guid CorrelationId = Guid.NewGuid();
	static readonly string IndexName = $"orders-by-country-{CorrelationId}";
	static readonly string Category = $"Orders_{CorrelationId:N}";
	static readonly string EventType = $"OrderCreated-{CorrelationId}";
	static readonly string Stream = $"{Category}-{CorrelationId}";

	[Test]
	public async ValueTask can_subscribe(CancellationToken ct) {
		// events that existed before index
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "A", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "B", "country": "United Kingdom" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "C", "country": "Mauritius" }""", ct);

		// create index
		await IndexesClient.CreateAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.schema.name == '{EventType}'",
				Fields = {
					new IndexField {
						Name = "country",
						Selector = "rec => rec.value.country",
						Type = IndexFieldType.String,
					},
				},
			},
			cancellationToken: ct);

		var allFields = $"$idx-user-{IndexName}";
		var mauritiusField = $"{allFields}:Mauritius";

		// wait for index to become available
		await StreamsReadClient.WaitForIndexEvents(allFields, 1, ct);
		await StreamsReadClient.WaitForIndexEvents(mauritiusField, 1, ct);

		// subscribe
		await using var allFieldsEnumerator = StreamsReadClient.SubscribeToAllFiltered(allFields, ct).GetAsyncEnumerator(ct);
		await using var mauritiusEnumerator = StreamsReadClient.SubscribeToAllFiltered(mauritiusField, ct).GetAsyncEnumerator(ct);

		await Assert.That((await allFieldsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "A", """);
		await Assert.That((await allFieldsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "B", """);
		await Assert.That((await allFieldsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "C", """);

		await Assert.That((await mauritiusEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "A", """);
		await Assert.That((await mauritiusEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "C", """);

		// write more and receive
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "D", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "E", "country": "United Kingdom" }""", ct);

		await Assert.That((await allFieldsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "D", """);
		await Assert.That((await allFieldsEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "E", """);

		await Assert.That((await mauritiusEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "D", """);

		// stop
		await IndexesClient.StopAsync(new() { Name = IndexName }, cancellationToken: ct);

		await Task.Delay(500); // todo better way to wait for it to stop

		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "F", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "G", "country": "United Kingdom" }""", ct);

		var nextAllResult = allFieldsEnumerator.ConsumeNext();
		var nextMauritiusResult = mauritiusEnumerator.ConsumeNext();
		await Task.Delay(500);

		await Assert.That(nextAllResult.IsCompleted).IsTrue();
		await Assert.That(nextMauritiusResult.IsCompleted).IsTrue();

		// start
		await IndexesClient.StartAsync(new() { Name = IndexName }, cancellationToken: ct);
		await Task.Delay(1000);

		// resubscribe and skip already processed events
		await using var allFieldsEnumerator2 = StreamsReadClient.SubscribeToAllFiltered(allFields, ct).GetAsyncEnumerator(ct);
		await using var mauritiusEnumerator2 = StreamsReadClient.SubscribeToAllFiltered(mauritiusField, ct).GetAsyncEnumerator(ct);

		await allFieldsEnumerator2.SkipAsync(5);
		await mauritiusEnumerator2.SkipAsync(3);

		// receive the extra events
		await Assert.That((await allFieldsEnumerator2.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "F", """);
		await Assert.That((await allFieldsEnumerator2.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "G", """);

		await Assert.That((await mauritiusEnumerator2.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "F", """);

		// delete
		await IndexesClient.DeleteAsync(new() { Name = IndexName }, cancellationToken: ct);

		var ex = await Assert
			.That(async () => await allFieldsEnumerator2.ConsumeNext())
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{allFields}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);

		ex = await Assert
			.That(async () => await mauritiusEnumerator2.ConsumeNext())
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{mauritiusField}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	[Arguments("")]
	[Arguments("Mauritius")]
	public async ValueTask cannot_subscribe_to_non_existent_index(string field, CancellationToken ct) {
		var fieldSuffix = field is "" ? "" : $":{field}";
		var index = $"$idx-user-does-not-exist{fieldSuffix}";
		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					.SubscribeToAllFiltered(index, ct)
					.ToArrayAsync(ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{index}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	[Arguments("")]
	[Arguments("Mauritius")]
	public async ValueTask cannot_subscribe_to_malformed_index(string field, CancellationToken ct) {
		var fieldSuffix = field is "" ? "" : $":{field}";
		var index = $"$idx-woops{fieldSuffix}";
		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					.SubscribeToAllFiltered(index, ct)
					.ToArrayAsync(ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{index}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}
}
