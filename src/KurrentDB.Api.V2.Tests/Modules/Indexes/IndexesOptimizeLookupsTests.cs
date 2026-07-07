// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public class IndexesOptimizeLookupsTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient IndexesClient => KurrentContext.IndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	static readonly Guid CorrelationId = Guid.NewGuid();
	static readonly string IndexName = $"orders-optimized-{CorrelationId}";
	static readonly string Category = $"OptimizedOrders_{CorrelationId:N}";
	static readonly string EventType = $"OrderPlaced-{CorrelationId}";
	static readonly string Stream = $"{Category}-{CorrelationId}";
	static readonly string ReadFilter = $"$idx-user-{IndexName}";

	[Test]
	public async ValueTask can_setup(CancellationToken ct) {
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "A", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "B", "country": "United Kingdom" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "C", "country": "Mauritius" }""", ct);

		// optimize_lookups builds a DuckDB ART index on the column; results must be identical to a plain (scanned) field
		await IndexesClient.CreateAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.schema.name == '{EventType}'",
				Fields = {
					new IndexField {
						Name = "country",
						Selector = "rec => rec.value.country",
						Type = IndexFieldType.String,
						OptimizeLookups = true,
					},
				},
			},
			cancellationToken: ct);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(50)] // because index processing is asynchronous to the write
	[Arguments("", 3)]
	[Arguments(":country=\"Mauritius\"", 2)]
	[Arguments(":country=\"United Kingdom\"", 1)]
	[Arguments(":country=\"France\"", 0)]
	public async ValueTask optimized_field_returns_correct_results(string suffix, int expectedCount, CancellationToken ct) {
		var events = await StreamsReadClient
			.ReadAllForwardFiltered($"{ReadFilter}{suffix}", ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(expectedCount);
	}
}
