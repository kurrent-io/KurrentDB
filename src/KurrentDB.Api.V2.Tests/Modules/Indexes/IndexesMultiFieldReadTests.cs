// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Client;
using Grpc.Core;
using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public class IndexesMultiFieldReadTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient IndexesClient => KurrentContext.IndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	static readonly Guid CorrelationId = Guid.NewGuid();
	static readonly string IndexName = $"orders-by-country-and-amount-{CorrelationId}";
	static readonly string Category = $"MultiOrders_{CorrelationId:N}";
	static readonly string EventType = $"OrderPlaced-{CorrelationId}";
	static readonly string Stream = $"{Category}-{CorrelationId}";
	static readonly string ReadFilter = $"$idx-user-{IndexName}";

	[Test]
	public async ValueTask can_setup(CancellationToken ct) {
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "A", "country": "Mauritius", "amount": 100 }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "B", "country": "United Kingdom", "amount": 200 }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "C", "country": "Mauritius", "amount": 100 }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "D", "country": "Mauritius" }""", ct); // no amount -> NULL country present
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "E" }""", ct); // no fields at all -> dropped
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "F", "amount": 300 }""", ct); // no country -> NULL amount present (symmetric to D)

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
					new IndexField {
						Name = "amount",
						Selector = "rec => rec.value.amount",
						Type = IndexFieldType.Int32,
					}
				}
			},
			cancellationToken: ct);

		// block until the index has processed every indexable event (A,B,C,D,F; E is dropped),
		// so the reads below run against a settled index and don't need to retry
		await StreamsReadClient.WaitForIndexEvents(ReadFilter, 5, ct);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Arguments("", "A,B,C,D,F")] // whole index (E dropped - all fields null)
	[Arguments(":country=\"Mauritius\"", "A,C,D")] // F excluded: its country is NULL
	[Arguments(":amount=100", "A,C")] // D excluded: its amount is NULL
	[Arguments(":country=\"Mauritius\";amount=100", "A,C")]
	[Arguments(":amount=100;country=\"Mauritius\"", "A,C")] // order does not matter
	[Arguments(":country=\"United Kingdom\";amount=200", "B")]
	[Arguments(":amount=300", "F")] // matches F, whose country is NULL (symmetric to D)
	[Arguments(":amount=999", "")] // no match
	[Arguments(":country=\"France\"", "")] // no match
	public async ValueTask can_read_field_subsets_forwards(string suffix, string expectedOrderIds, CancellationToken ct) {
		var events = await StreamsReadClient
			.ReadAllForwardFiltered($"{ReadFilter}{suffix}", ct)
			.ToArrayAsync(ct);

		await AssertMatches(events, expectedOrderIds, ascending: true);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Arguments(":Mauritius")] // legacy bare value is not valid on a multi-field index
	[Arguments(":nosuchfield=1")] // unknown field
	[Arguments(":amount=notanumber")] // value not convertible to the field's type
	[Arguments(":amount=1;amount=2")] // duplicate field
	[Arguments(":country=\"Mauritius\";amount")] // malformed pair (missing '=')
	public async ValueTask invalid_field_constraints_are_rejected(string suffix, CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					.ReadAllForwardFiltered($"{ReadFilter}{suffix}", ct)
					.ToArrayAsync(ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Arguments("", "A,B,C,D,F")] // whole index
	[Arguments(":country=\"Mauritius\"", "A,C,D")] // single field
	[Arguments(":country=\"Mauritius\";amount=100", "A,C")]
	[Arguments(":amount=200", "B")]
	[Arguments(":amount=999", "")] // no match
	public async ValueTask can_read_field_subsets_backwards(string suffix, string expectedOrderIds, CancellationToken ct) {
		var events = await StreamsReadClient
			.ReadAllBackwardFiltered($"{ReadFilter}{suffix}", ct)
			.ToArrayAsync(ct);

		await AssertMatches(events, expectedOrderIds, ascending: false);
	}

	// Asserts the read returned exactly the expected events (by orderId) and in the expected log order.
	private static async Task AssertMatches(IReadOnlyList<EventRecord> events, string expectedOrderIds, bool ascending) {
		var expected = expectedOrderIds.Length == 0 ? [] : expectedOrderIds.Split(',');

		await Assert.That(events.Count).IsEqualTo(expected.Length);

		foreach (var orderId in expected)
			await Assert.That(events.Any(e => e.Data.ToStringUtf8().Contains($"\"orderId\": \"{orderId}\""))).IsTrue();

		if (ascending)
			await Assert.That(events).IsOrderedBy(x => x.EventNumber);
		else
			await Assert.That(events).IsOrderedByDescending(x => x.EventNumber);
	}
}
