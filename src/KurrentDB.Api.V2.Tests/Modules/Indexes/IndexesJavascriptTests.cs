// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public class IndexesJavascriptTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient IndexesClient => KurrentContext.IndexesClient;
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
		await IndexesClient.CreateAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.schema.name == '{EventType}'",
				Fields = {
					new IndexField {
						Name = "color",
						Selector = """
							rec => {
								let color = rec.value.color;
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
		await IndexesClient.CreateAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.schema.name == '{EventType}'",
				Fields = {
					new IndexField() {
						Name = "color",
						Selector = "rec => rec.value.color",
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
		await IndexesClient.CreateAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.schema.name == '{EventType}'",
			},
			cancellationToken: ct);

		await StreamsWriteClient.AppendEvent(Stream, EventType, $$"""{ "orderId": "A" }""", ct);

		var evts = await StreamsReadClient.WaitForIndexEvents(ReadFilter, 1, ct);
		await Assert.That(evts.Count).IsEqualTo(1);
		await Assert.That(evts[0].Data.ToStringUtf8()).Contains(""" "orderId": "A" """);
	}

	[Test]
	[Arguments("stream", "rec => rec.position.stream[0]", IndexFieldType.String, "O")]
	[Arguments("number", "rec => rec.position.streamRevision", IndexFieldType.Int32, "0")]
	[Arguments("type", "rec => rec.schema.name[0]", IndexFieldType.String, "O")]
	[Arguments("data", "rec => JSON.stringify(rec.value)[0]", IndexFieldType.String, "{")]
	[Arguments("metadata", "rec => JSON.stringify(rec.properties)[0]", IndexFieldType.String, "{")]
	[Arguments("id", "rec => rec.id.length", IndexFieldType.Int32, "36")]
	[Arguments("schema-name", "rec => rec.schema.format", IndexFieldType.String, "Json")]
	[Arguments("log-position", "rec => rec.position.logPosition > 0 ? 1 : 0", IndexFieldType.Int32, "1")]
	[Arguments("is-redacted", "rec => rec.redacted.toString()", IndexFieldType.String, "false")]
	[Arguments("sequence-id", "rec => rec.sequence > 0 ? 1 : 0", IndexFieldType.Int32, "1")]
	public async ValueTask can_select_record_properties(string fieldName, string fieldSelector, IndexFieldType fieldType, string fieldFilter, CancellationToken ct) {
		await IndexesClient.CreateAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.schema.name == '{EventType}'",
				Fields = {
					new IndexField {
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

	[Test]
	// data tests
	[Arguments(SchemaFormat.Json, "rec => (rec.value.color == 'red').toString()")]
	[Arguments(SchemaFormat.Bytes, "rec => (rec.value == undefined).toString()")]
	[Arguments(SchemaFormat.Avro, "rec => (rec.value == undefined).toString()")]
	[Arguments(SchemaFormat.Protobuf, "rec => (rec.value == undefined).toString()")]
	// properties tests
	[Arguments(SchemaFormat.Json, "rec => (rec.properties.key == 'value').toString()")]
	[Arguments(SchemaFormat.Bytes, "rec => (rec.properties.key == 'value').toString()")]
	[Arguments(SchemaFormat.Avro, "rec => (rec.properties.key == 'value').toString()")]
	[Arguments(SchemaFormat.Protobuf, "rec => (rec.properties.key == 'value').toString()")]
	// synthesized properties tests
	[Arguments(SchemaFormat.Json, "rec => (rec.properties['$schema.format'] == 'Json').toString()")]
	[Arguments(SchemaFormat.Bytes, "rec => (rec.properties['$schema.format'] == 'Bytes').toString()")]
	[Arguments(SchemaFormat.Avro, "rec => (rec.properties['$schema.format'] == 'Avro').toString()")]
	[Arguments(SchemaFormat.Protobuf, "rec => (rec.properties['$schema.format'] == 'Protobuf').toString()")]
	// schema type tests
	[Arguments(SchemaFormat.Json, "rec => (rec.schema.format == 'Json').toString()")]
	[Arguments(SchemaFormat.Bytes, "rec => (rec.schema.format == 'Bytes').toString()")]
	[Arguments(SchemaFormat.Avro, "rec => (rec.schema.format == 'Avro').toString()")]
	[Arguments(SchemaFormat.Protobuf, "rec => (rec.schema.format == 'Protobuf').toString()")]
	public async ValueTask can_select_record_with_different_data_formats(SchemaFormat format, string selector, CancellationToken ct) {
		await IndexesClient.CreateAsync(
			new() {
				Name = IndexName,
				Filter = $"rec => rec.schema.name == '{EventType}'",
				Fields = {
					new IndexField() {
						Name = "field",
						Selector = selector,
						Type = IndexFieldType.String,
					},
				}
			},
			cancellationToken: ct);

		var props = new Dictionary<string, string> {
			["key"] = "value"
		};

		await StreamsWriteClient.AppendRecord(Stream, EventType, format, "{ \"color\": \"red\" }"u8, props, ct);

		var evts = await StreamsReadClient.WaitForIndexEvents($"$idx-user-{IndexName}", 1, ct);
		await Assert.That(evts.Count).IsEqualTo(1);
	}
}
