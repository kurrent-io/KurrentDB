// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Data;

// EventRecord.Metadata synthesis from log-record Properties (the properties -> JSON path). The empty-Value
// case used to throw straight out of the .Metadata getter — and because that getter is read on the projection
// core thread, an unhandled throw took the whole node down. These pin the happy path and the crash regression.
public class EventRecordPropertyMetadataTests {
	static EventRecord PropertyRecord(Struct properties, bool isJson) =>
		new(
			eventNumber: 0,
			logPosition: 0,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			transactionPosition: 0,
			transactionOffset: 0,
			eventStreamId: "test-stream",
			expectedVersion: 0,
			timeStamp: DateTime.UtcNow,
			flags: PrepareFlags.IsPropertyMetadata | (isJson ? PrepareFlags.IsJson : PrepareFlags.None),
			eventType: "TestType",
			data: [1, 2, 3],
			metadata: properties.ToByteArray());

	[Fact]
	public void Synthesizes_json_metadata_from_properties() {
		var record = PropertyRecord(new Struct {
			Fields = {
				["user-id"] = Value.ForString("12345"),
				["count"] = Value.ForNumber(42),
			}
		}, isJson: true);

		using var doc = JsonDocument.Parse(record.Metadata);
		var root = doc.RootElement;

		Assert.Equal("12345", root.GetProperty("user-id").GetString());
		Assert.Equal("TestType", root.GetProperty("$schema.name").GetString()); // schema name = event type
		Assert.Equal("Json", root.GetProperty("$schema.format").GetString());
		Assert.Equal("Json", record.SchemaFormat);
	}

	// Regression: a property whose google.protobuf.Value has no kind set (an empty Value) made the JSON
	// formatter throw "Value message must contain a value for the oneof". Reading must degrade to a visible
	// marker rather than throw (which previously crashed projections, and so the node).
	[Fact]
	public void Empty_property_value_is_surfaced_instead_of_throwing() {
		var record = PropertyRecord(new Struct {
			Fields = {
				["good"] = Value.ForString("ok"),
				["bad"] = new Value(),   // no kind set in the oneof
			}
		}, isJson: true);

		var json = Encoding.UTF8.GetString(record.Metadata.Span);   // before the fix this threw

		Assert.Contains("$propertiesError", json);
		using var _ = JsonDocument.Parse(json);   // and still emits valid JSON
	}

	[Fact]
	public void Non_json_record_with_schema_format_reads_it() {
		var record = PropertyRecord(new Struct {
			Fields = {
				["k"] = Value.ForString("v"),
				["$schema.format"] = Value.ForString("Bytes"),
			}
		}, isJson: false);

		var json = Encoding.UTF8.GetString(record.Metadata.Span);

		Assert.Equal("Bytes", record.SchemaFormat);
		Assert.Contains("\"k\"", json);
	}
}
