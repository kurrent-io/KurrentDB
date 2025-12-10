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
	public async ValueTask can_filter_by_skipping(CancellationToken ct) {
		await Client.CreateCustomIndexAsync(
			new() {
				Name = CustomIndexName,
				Filter = $"rec => rec.type == '{EventType}'",
				Fields = {
					new Field {
						Name = "color",
						Selector = """
							rec => {
								let color = rec.data.color;
								if (color == 'green')
									return skip;
								return color;
							}
							""",
						Type = FieldType.String,

					},
				},
			},
			cancellationToken: ct);

		// write an event that doesn't pass the filter
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A1", "color": "green" }""", ct);
		// write an event that passes the filter
		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "B", "color": "blue" }""", ct);

		// ensure the index only contains the one event
		await StreamsReadClient.WaitForCustomIndexEvents(ReadFilter, 1, ct);
		var evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);

		// ensure blue only contains the one event
		await StreamsReadClient.WaitForCustomIndexEvents($"{ReadFilter}:blue", 1, ct);
		evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}:blue", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);

		// ensure green contains no events
		evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}:green", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(FieldType.String, """ "blue" """, """ "red" """, "red")]
	[Arguments(FieldType.String, """ "blue:2" """, """ "red:3" """, "red:3")]

	[Arguments(FieldType.Int16,  """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(FieldType.Int32,  """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(FieldType.Int64,  """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(FieldType.Uint32, """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(FieldType.Uint64, """ 0.0 """, """ 1.0 """, "1")]

	[Arguments(FieldType.Double, """ 0   """, """ 1   """, "1")]
	[Arguments(FieldType.Double, """ 0.0 """, """ 1.0 """, "1")]
	[Arguments(FieldType.Double, """ 1234.56 """, """ 6543.21 """, "6543.21")]
	[Arguments(FieldType.Double,        """ 1234.56 """, """ 6543.21 """, "6543.210")]
	public async ValueTask can_use_all_field_types(FieldType fieldType, string field1, string field2, string fieldFilter, CancellationToken ct) {
		await Client.CreateCustomIndexAsync(
			new() {
				Name = CustomIndexName,
				Filter = $"rec => rec.type == '{EventType}'",
				Fields = {
					new Field() {
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

		// ensure both events are processed by the custom index
		var evts = await StreamsReadClient.WaitForCustomIndexEvents(ReadFilter, 2, ct);
		await Assert.That(evts.Count).IsEqualTo(2);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "A", """);
		await Assert.That(evts[1].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);

		// ensure the target field only contains the one event
		await StreamsReadClient.WaitForCustomIndexEvents($"{ReadFilter}:{fieldFilter}", 1, ct);
		evts = await StreamsReadClient.ReadAllForwardFiltered($"{ReadFilter}:{fieldFilter}", ct).ToArrayAsync(ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "B", """);
	}

	[Test]
	public async ValueTask can_use_null_field_type(CancellationToken ct) {
		await Client.CreateCustomIndexAsync(
			new() {
				Name = CustomIndexName,
				Filter = $"rec => rec.type == '{EventType}'",
				Fields = {
					new Field() {
						Name = "null", //qq we could consider making name unnecessary when there is only one field
						Selector = "rec => null", //qq todo: when the field type is null we probably shouldn't call the selector
						Type = FieldType.Null,
					},
				},
			},
			cancellationToken: ct);

		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A" }""", ct);

		var evts = await StreamsReadClient.WaitForCustomIndexEvents(ReadFilter, 1, ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "A" """);
	}
}
