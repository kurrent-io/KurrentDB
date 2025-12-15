// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public class IndexesReadTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient Client => KurrentContext.IndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	static readonly Guid CorrelationId = Guid.NewGuid();
	static readonly string IndexName = $"orders-by-country-{CorrelationId}";
	static readonly string Category = $"Orders_{CorrelationId:N}";
	static readonly string EventType = $"OrderCreated-{CorrelationId}";
	static readonly string Stream = $"{Category}-{CorrelationId}";
	static readonly string ReadFilter = $"$idx-user-{IndexName}";
	static readonly string CategoryFilter = $"$idx-ce-{Category}";
	static readonly string EventTypeFilter = $"$idx-et-{EventType}";


	[Test]
	public async ValueTask can_setup(CancellationToken ct) {
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "A", "country": "Mauritius" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "B", "country": "United Kingdom" }""", ct);
		await StreamsWriteClient.AppendEvent(Stream, EventType, """{ "orderId": "C", "country": "Mauritius" }""", ct);

		await Client.CreateIndexAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.type == '{EventType}'",
				Fields = {
					new Field() {
						Name = "country",
						Selector = "rec => rec.data.country",
						Type = FieldType.String,
					}
				}
			},
			cancellationToken: ct);
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(50)] // because index processing is asynchronous to the write
	[Arguments("", 3)]
	[Arguments("Mauritius", 2)]
	[Arguments("United Kingdom", 1)]
	[Arguments("united kingdom", 0)]
	public async ValueTask can_read_index_forwards(string field, int expectedCount, CancellationToken ct) {
		var fieldSuffix = field is "" ? "" : $":{field}";
		var events = await StreamsReadClient
			.ReadAllForwardFiltered($"{ReadFilter}{fieldSuffix}", ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(expectedCount);
		await Assert.That(events).IsOrderedBy(x => x.EventNumber);
		if (field is not "")
			await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains($""" "country": "{field}" """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(50)] // because index processing is asynchronous to the write
	[Arguments("", 3)]
	[Arguments("Mauritius", 2)]
	[Arguments("United Kingdom", 1)]
	[Arguments("united kingdom", 0)]
	public async ValueTask can_read_index_backwards(string field, int expectedCount, CancellationToken ct) {
		var fieldSuffix = field is "" ? "" : $":{field}";
		var events = await StreamsReadClient
			.ReadAllBackwardFiltered($"{ReadFilter}{fieldSuffix}", ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(expectedCount);
		await Assert.That(events).IsOrderedByDescending(x => x.EventNumber);
		if (field is not "")
			await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains($""" "country": "{field}" """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(50)] // because index processing is asynchronous to the write
	public async ValueTask can_read_category_index_forwards(CancellationToken ct) {

		var events = await StreamsReadClient
			.ReadAllForwardFiltered(CategoryFilter, ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedBy(x => x.Position.PreparePosition);
		await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains(""" "orderId": """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(50)] // because index processing is asynchronous to the write
	public async ValueTask can_read_category_index_backwards(CancellationToken ct) {
		var events = await StreamsReadClient
			.ReadAllBackwardFiltered(CategoryFilter, ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedByDescending(x => x.Position.PreparePosition);
		await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains(""" "orderId": """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(50)] // because index processing is asynchronous to the write
	public async ValueTask can_read_event_type_index_forwards(CancellationToken ct) {

		var events = await StreamsReadClient
			.ReadAllForwardFiltered(EventTypeFilter, ct)
			.ToArrayAsync(ct);

		await Assert.That(events.Count).IsEqualTo(3);
		await Assert.That(events).IsOrderedBy(x => x.Position.PreparePosition);
		await Assert.That(events).All(x => x.Data.ToStringUtf8().Contains(""" "orderId": """));
	}

	[Test]
	[DependsOn(nameof(can_setup))]
	[Retry(50)] // because index processing is asynchronous to the write
	public async ValueTask can_read_event_type_index_backwards(CancellationToken ct) {

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
	public async ValueTask cannot_read_non_existent_index(string field, CancellationToken ct) {
		var fieldSuffix = field is "" ? "" : $":{field}";
		var index = $"$idx-user-does-not-exist{fieldSuffix}";
		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					.ReadAllForwardFiltered(index, ct)
					.ToArrayAsync(ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{index}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	[Arguments("")]
	[Arguments("Mauritius")]
	public async ValueTask cannot_read_malformed_index(string field, CancellationToken ct) {
		var fieldSuffix = field is "" ? "" : $":{field}";
		var index = $"$idx-woops{fieldSuffix}";
		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					.ReadAllForwardFiltered(index, ct)
					.ToArrayAsync(ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{index}' not found.");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}
}
