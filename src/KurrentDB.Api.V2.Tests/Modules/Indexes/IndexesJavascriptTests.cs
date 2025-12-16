// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.TUnit;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public class IndexesJavascriptTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient Client => KurrentContext.IndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	readonly Guid _correlationId = Guid.NewGuid();
	string IndexName => $"orders-by-country-{_correlationId}";
	string Category => $"Orders_{_correlationId:N}";
	string EventType => $"OrderCreated-{_correlationId}";
	string Stream => $"{Category}-{_correlationId}";
	string ReadFilter => $"$idx-user-{IndexName}";

	[Test]
	public async ValueTask can_filter_by_skipping(CancellationToken ct) {
		await Client.CreateIndexAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.type == '{EventType}'",
				Fields = {
					new IndexField {
						Name = "color",
						Selector = """
							rec => {
								let color = rec.data.color;
								if (color == 'green')
									return skip;
								return color;
							}
							""",
						Type = IndexFieldType.String,

					},
				},
			},
			cancellationToken: ct);

		// write an event that doesn't pass the filter
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A1", "color": "green" }""", ct);
		// write an event that passes the filter
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "B", "color": "blue" }""", ct);

		// ensure the index only contains the one event
		await StreamsReadClient.WaitForIndexEvents(ReadFilter, 1, ct);
		var evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);

		// ensure blue only contains the one event
		await StreamsReadClient.WaitForIndexEvents($"{ReadFilter}:blue", 1, ct);
		evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}:blue", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);

		// ensure green contains no events
		evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}:green", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(IndexFieldType.String, """ "blue" """, """ "red" """, "red")]
	[Arguments(IndexFieldType.String, """ "blue:2" """, """ "red:3" """, "red:3")]

//	[Arguments(IndexFieldType.Int16,  """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(IndexFieldType.Int32,  """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(IndexFieldType.Int64,  """ 0.0 """, """ 1.0 """, "1")]
//	[Arguments(IndexFieldType.Uint32, """ 0.0 """, """ 1.0 """, "1")]
//	[Arguments(IndexFieldType.Uint64, """ 0.0 """, """ 1.0 """, "1")]

	[Arguments(IndexFieldType.Double, """ 0   """, """ 1   """, "1")]
	[Arguments(IndexFieldType.Double, """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(IndexFieldType.Double, """ 1234.56 """, """ 6543.21 """, "6543.21")]
	[Arguments(IndexFieldType.Double,        """ 1234.56 """, """ 6543.21 """, "6543.210")]
	public async ValueTask can_use_all_field_types(IndexFieldType fieldType, string field1, string field2, string fieldFilter, CancellationToken ct) {
		await Client.CreateIndexAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.type == '{EventType}'",
				Fields = {
					new IndexField() {
						Name = "color",
						Selector = "rec => rec.data.color",
						Type = fieldType,
					},
				},
			},
			cancellationToken: ct);

		// write an event for one field
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A", "color": {{field1}} }""", ct);
		// write an event for another field
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "B", "color": {{field2}} }""", ct);

		// ensure both events are processed by the index
		var evts = await StreamsReadClient.WaitForIndexEvents(ReadFilter, 2, ct);
		await Assert.That(evts.Count).IsEqualTo(2);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "A", """);
		await Assert.That(evts[1].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);

		// ensure the target field only contains the one event
		await StreamsReadClient.WaitForIndexEvents($"{ReadFilter}:{fieldFilter}", 1, ct);
		evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}:{fieldFilter}", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);
	}

	[Test]
	public async ValueTask can_use_no_field(CancellationToken ct) {
		await Client.CreateIndexAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.type == '{EventType}'",
			},
			cancellationToken: ct);

		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A" }""", ct);

		var evts = await StreamsReadClient.WaitForIndexEvents(ReadFilter, 1, ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "A" """);
	}

	[Test]
	[Arguments("stream", "rec => rec.stream[0]", IndexFieldType.String, "O")]
	[Arguments("number", "rec => rec.number", IndexFieldType.Int32, "0")]
	[Arguments("type", "rec => rec.type[0]", IndexFieldType.String, "O")]
	[Arguments("data", "rec => JSON.stringify(rec.data)[0]", IndexFieldType.String, "{")]
	[Arguments("metadata", "rec => JSON.stringify(rec.metadata)[0]", IndexFieldType.String, "{")]
	[Arguments("raw-data", "rec => new Uint8Array(rec.rawData)[0]", IndexFieldType.Int32, "123")]
	[Arguments("raw-metadata", "rec => new Uint8Array(rec.rawMetadata)[0]", IndexFieldType.Int32, "123")]
	[Arguments("id", "rec => rec.id.length", IndexFieldType.Int32, "36")]
	[Arguments("json", "rec => rec.isJson ? 1 : 0", IndexFieldType.Int32, "1")]
	public async ValueTask can_select_record_properties(string fieldName, string fieldSelector, IndexFieldType fieldType, string fieldFilter, CancellationToken ct) {
		await Client.CreateIndexAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.type == '{EventType}'",
				Fields = {
					new IndexField() {
						Name = fieldName,
						Selector = fieldSelector,
						Type = fieldType,
					},
				}
			},
			cancellationToken: ct);

		await StreamsWriteClient.AppendEvent(Stream, EventType, "{}", ct);

		var evts = await StreamsReadClient.WaitForIndexEvents($"$idx-user-{IndexName}:{fieldFilter}", 1, ct);
		await Assert.That(evts.Count).IsEqualTo(1);
	}
}
