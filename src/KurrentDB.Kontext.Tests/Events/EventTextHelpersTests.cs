// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;

namespace KurrentDB.Kontext.Tests.Events;

public class EventTextHelpersTests {
	static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

	static string BuildEventText(string eventType, ReadOnlySpan<byte> data) =>
		EventTextHelpers.BuildEventText(eventType, data, new StringBuilder());

	static (string Keys, string Values) BuildFlatKeyValueTexts(string eventType, ReadOnlySpan<byte> data) =>
		EventTextHelpers.BuildFlatKeyValueTexts(eventType, data, new StringBuilder(), new StringBuilder());

	// -------- SplitName --------

	[Test]
	public async Task SplitName_PascalCase() {
		var result = EventTextHelpers.SplitName("OrderPlaced");
		await Assert.That(result).IsEqualTo("order placed");
	}

	[Test]
	public async Task SplitName_SnakeCase() {
		var result = EventTextHelpers.SplitName("order_placed");
		await Assert.That(result).IsEqualTo("order placed");
	}

	[Test]
	public async Task SplitName_KebabCase() {
		var result = EventTextHelpers.SplitName("order-placed");
		await Assert.That(result).IsEqualTo("order placed");
	}

	[Test]
	public async Task SplitName_SingleWord() {
		var result = EventTextHelpers.SplitName("Order");
		await Assert.That(result).IsEqualTo("order");
	}

	// -------- BuildFlatKeyValueTexts: keys --------

	[Test]
	public async Task BuildKeyValueTexts_EventType_Only_No_Duplication() {
		var (keys, _) = BuildFlatKeyValueTexts("OrderPlaced", []);
		await Assert.That(keys).IsEqualTo("order placed");
	}

	[Test]
	public async Task BuildKeyValueTexts_Includes_Json_Keys_Not_Values() {
		var (keys, _) = BuildFlatKeyValueTexts("OrderPlaced",
			Bytes("""{"customer":"Alice","product":"Widget"}"""));

		await Assert.That(keys).Contains("order placed");
		await Assert.That(keys).Contains("customer");
		await Assert.That(keys).Contains("product");
		await Assert.That(keys).DoesNotContain("Alice");
		await Assert.That(keys).DoesNotContain("Widget");
	}

	[Test]
	public async Task BuildKeyValueTexts_Flattens_Nested_Keys() {
		var (keys, _) = BuildFlatKeyValueTexts("OrderPlaced",
			Bytes("""{"address":{"city":"Portland","state":"OR"}}"""));

		await Assert.That(keys).Contains("address");
		await Assert.That(keys).Contains("city");
		await Assert.That(keys).Contains("state");
		await Assert.That(keys).DoesNotContain("Portland");
		await Assert.That(keys).DoesNotContain("OR");
	}

	[Test]
	public async Task BuildKeyValueTexts_Splits_Keys_By_Naming_Convention() {
		var (keys, _) = BuildFlatKeyValueTexts("OrderPlaced",
			Bytes("""{"shippingAddress":"123 Main St"}"""));

		await Assert.That(keys).Contains("shipping address");
		await Assert.That(keys).DoesNotContain("123 Main St");
	}

	[Test]
	public async Task BuildKeyValueTexts_Deeply_Nested_Keys() {
		var (keys, _) = BuildFlatKeyValueTexts("Event",
			Bytes("""{"level1":{"level2":{"level3":"deep value"}}}"""));

		await Assert.That(keys).Contains("level1");
		await Assert.That(keys).Contains("level2");
		await Assert.That(keys).Contains("level3");
		await Assert.That(keys).DoesNotContain("deep value");
	}

	// -------- BuildFlatKeyValueTexts: values --------

	[Test]
	public async Task BuildKeyValueTexts_Extracts_String_Values() {
		var (_, values) = BuildFlatKeyValueTexts("Event",
			Bytes("""{"customer":"Alice","product":"Widget"}"""));

		await Assert.That(values).Contains("Alice");
		await Assert.That(values).Contains("Widget");
		await Assert.That(values).DoesNotContain("customer");
		await Assert.That(values).DoesNotContain("product");
	}

	[Test]
	public async Task BuildKeyValueTexts_Flattens_Nested_Values() {
		var (_, values) = BuildFlatKeyValueTexts("Event",
			Bytes("""{"address":{"city":"Portland","state":"OR"}}"""));

		await Assert.That(values).Contains("Portland");
		await Assert.That(values).Contains("OR");
	}

	[Test]
	public async Task BuildKeyValueTexts_Flattens_Arrays() {
		var (_, values) = BuildFlatKeyValueTexts("Event",
			Bytes("""{"tags":["urgent","fragile"]}"""));

		await Assert.That(values).Contains("urgent");
		await Assert.That(values).Contains("fragile");
	}

	[Test]
	public async Task BuildKeyValueTexts_Skips_Nulls_And_Empty_Strings() {
		var (_, values) = BuildFlatKeyValueTexts("Event",
			Bytes("""{"name":"Alice","middle":null,"suffix":"","note":"   "}"""));

		await Assert.That(values).Contains("Alice");
		await Assert.That(values).DoesNotContain("middle");
		await Assert.That(values).DoesNotContain("suffix");
		await Assert.That(values).DoesNotContain("note");
	}

	[Test]
	public async Task BuildKeyValueTexts_Skips_Booleans() {
		var (_, values) = BuildFlatKeyValueTexts("Event",
			Bytes("""{"name":"Alice","active":true,"deleted":false}"""));

		await Assert.That(values).Contains("Alice");
		await Assert.That(values).DoesNotContain("True");
		await Assert.That(values).DoesNotContain("False");
		await Assert.That(values).DoesNotContain("true");
		await Assert.That(values).DoesNotContain("false");
	}

	[Test]
	public async Task BuildKeyValueTexts_Includes_Numbers() {
		var (_, values) = BuildFlatKeyValueTexts("Event",
			Bytes("""{"quantity":42,"price":19.99}"""));

		await Assert.That(values).Contains("42");
		await Assert.That(values).Contains("19.99");
	}

	[Test]
	public async Task BuildKeyValueTexts_Empty_Data_Returns_Only_EventType() {
		var (keys, values) = BuildFlatKeyValueTexts("Event", []);
		await Assert.That(keys).IsEqualTo("event");
		await Assert.That(values).IsEqualTo("");
	}

	[Test]
	public async Task BuildKeyValueTexts_Invalid_Json_Yields_Only_EventType() {
		var (keys, values) = BuildFlatKeyValueTexts("Event", Bytes("not-json"));
		await Assert.That(keys).IsEqualTo("event");
		await Assert.That(values).IsEqualTo("");
	}

	// -------- Non-object root JSON --------

	[Test]
	public async Task BuildKeyValueTexts_String_Root_Adds_To_Values_No_Keys() {
		var (keys, values) = BuildFlatKeyValueTexts("TestEvent",
			Bytes("""  "hello world"  """));

		await Assert.That(keys).IsEqualTo("test event");
		await Assert.That(values).Contains("hello world");
	}

	[Test]
	public async Task BuildKeyValueTexts_Array_Root_Adds_All_Items_To_Values() {
		var (_, values) = BuildFlatKeyValueTexts("Event",
			Bytes("""["one","two","three"]"""));

		await Assert.That(values).Contains("one");
		await Assert.That(values).Contains("two");
		await Assert.That(values).Contains("three");
	}

	[Test]
	public async Task BuildKeyValueTexts_Number_Root_Adds_To_Values() {
		var (_, values) = BuildFlatKeyValueTexts("Event", Bytes("42"));
		await Assert.That(values).Contains("42");
	}

	// -------- BuildEventText --------

	[Test]
	public async Task BuildEventText_Includes_EventType_And_KeyValuePairs() {
		var result = BuildEventText("OrderPlaced",
			Bytes("""{"customer":"Alice","total":99.5}"""));

		await Assert.That(result).Contains("order placed");
		await Assert.That(result).Contains("customer: Alice");
		await Assert.That(result).Contains("total: 99.5");
	}

	[Test]
	public async Task BuildEventText_Nested_Objects() {
		var result = BuildEventText("OrderPlaced",
			Bytes("""{"address":{"city":"Portland","state":"OR"}}"""));

		await Assert.That(result).Contains("address:");
		await Assert.That(result).Contains("city: Portland");
		await Assert.That(result).Contains("state: OR");
	}

	[Test]
	public async Task BuildEventText_Arrays() {
		var result = BuildEventText("OrderPlaced",
			Bytes("""{"tags":["urgent","fragile"]}"""));

		await Assert.That(result).Contains("tags:");
		await Assert.That(result).Contains("- urgent");
		await Assert.That(result).Contains("- fragile");
	}

	[Test]
	public async Task BuildEventText_Includes_Booleans_And_Nulls() {
		var result = BuildEventText("Event",
			Bytes("""{"name":"Alice","middle":null,"active":true,"deleted":false}"""));

		await Assert.That(result).Contains("name: Alice");
		await Assert.That(result).Contains("middle: null");
		await Assert.That(result).Contains("active: true");
		await Assert.That(result).Contains("deleted: false");
	}

	[Test]
	public async Task BuildEventText_Includes_Empty_Strings() {
		var result = BuildEventText("Event",
			Bytes("""{"name":"Alice","note":"","tags":["x","","y"]}"""));

		await Assert.That(result).Contains("name: Alice");
		await Assert.That(result).Contains("note:");
		await Assert.That(result).Contains("- x");
		await Assert.That(result).Contains("- y");
		// Array structure preserved: three "- " entries (one per element, even the empty one).
		await Assert.That(result.Split("- ").Length - 1).IsEqualTo(3);
	}

	[Test]
	public async Task BuildEventText_No_Data() {
		var result = BuildEventText("OrderPlaced", []);
		await Assert.That(result).IsEqualTo("order placed");
	}

	[Test]
	public async Task BuildEventText_String_Root() {
		var result = BuildEventText("TestEvent", Bytes("\"hello world\""));
		await Assert.That(result).Contains("test event");
		await Assert.That(result).Contains("hello world");
	}

	[Test]
	public async Task BuildEventText_Number_Root() {
		var result = BuildEventText("TestEvent", Bytes("42"));
		await Assert.That(result).Contains("test event");
		await Assert.That(result).Contains("42");
	}

	[Test]
	public async Task BuildEventText_Boolean_Root() {
		var result = BuildEventText("TestEvent", Bytes("true"));
		await Assert.That(result).Contains("test event");
		await Assert.That(result).Contains("true");
	}

	[Test]
	public async Task BuildEventText_Null_Root() {
		var result = BuildEventText("TestEvent", Bytes("null"));
		await Assert.That(result).Contains("test event");
		await Assert.That(result).Contains("null");
	}

	[Test]
	public async Task BuildEventText_Invalid_Json_Yields_Only_EventType() {
		var result = BuildEventText("OrderPlaced", Bytes("not-json"));
		await Assert.That(result).IsEqualTo("order placed");
	}
}