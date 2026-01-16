// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using KurrentDB.Connectors.Infrastructure;
using Microsoft.Extensions.Configuration;
using static KurrentDB.Connectors.Infrastructure.ConfigurationExtensions;

namespace KurrentDB.Connectors.Tests.Infrastructure;

public class ConfigurationExtensionsTests {
	[Fact]
	public void should_convert_flat_configuration_to_protobuf_struct() {
		// Arrange
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "key1", "value1" },
				{ "key2", "123" },
				{ "key3", "true" }
			})
			.Build();

		// Act
		var result = config.ToProtobufStruct();

		// Assert
		result.Fields.Should().HaveCount(3);
		result.Fields["key1"].StringValue.Should().Be("value1");
		result.Fields["key2"].NumberValue.Should().Be(123);
		result.Fields["key3"].BoolValue.Should().BeTrue();
	}

	[Fact]
	public void should_convert_nested_configuration_to_protobuf_struct() {
		// Arrange
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "parent:child1", "value1" },
				{ "parent:child2", "42" },
				{ "parent:nested:key", "nestedValue" }
			})
			.Build();

		// Act
		var result = config.ToProtobufStruct();

		// Assert
		result.Fields.Should().ContainKey("parent");
		var parent = result.Fields["parent"].StructValue;
		parent.Fields.Should().ContainKey("child1");
		parent.Fields["child1"].StringValue.Should().Be("value1");
		parent.Fields["child2"].NumberValue.Should().Be(42);

		parent.Fields.Should().ContainKey("nested");
		var nested = parent.Fields["nested"].StructValue;
		nested.Fields["key"].StringValue.Should().Be("nestedValue");
	}

	[Fact]
	public void should_convert_array_configuration_to_protobuf_list() {
		// Arrange
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "items:0", "first" },
				{ "items:1", "second" },
				{ "items:2", "third" }
			})
			.Build();

		// Act
		var result = config.ToProtobufStruct();

		// Assert
		result.Fields.Should().ContainKey("items");
		var items = result.Fields["items"].ListValue;
		items.Values.Should().HaveCount(3);
		items.Values[0].StringValue.Should().Be("first");
		items.Values[1].StringValue.Should().Be("second");
		items.Values[2].StringValue.Should().Be("third");
	}

	[Fact]
	public void should_omit_null_and_empty_values() {
		// Arrange
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "key1", "value1" },
				{ "key2", null },
				{ "key3", "" },
				{ "key4", "value4" }
			})
			.Build();

		// Act
		var result = config.ToProtobufStruct();

		// Assert
		result.Fields.Should().HaveCount(2);
		result.Fields.Should().ContainKey("key1");
		result.Fields.Should().ContainKey("key4");
		result.Fields.Should().NotContainKey("key2");
		result.Fields.Should().NotContainKey("key3");
	}

	[Fact]
	public void should_flatten_struct_to_dictionary() {
		// Arrange
		var structValue = new Struct {
			Fields = {
				{ "key1", Value.ForString("value1") },
				{ "key2", Value.ForNumber(42) },
				{ "nested", Value.ForStruct(new Struct {
					Fields = {
						{ "nestedKey", Value.ForBool(true) }
					}
				}) }
			}
		};

		// Act
		var result = structValue.FlattenStruct();

		// Assert
		result.Should().HaveCount(3);
		result["key1"].Should().Be("value1");
		result["key2"].Should().Be("42");
		result["nested:nestedKey"].Should().Be("True");
	}

	[Fact]
	public void should_flatten_struct_with_arrays() {
		// Arrange
		var structValue = new Struct {
			Fields = {
				{ "items", Value.ForList(
					Value.ForString("item1"),
					Value.ForString("item2"),
					Value.ForNumber(3)
				)}
			}
		};

		// Act
		var result = structValue.FlattenStruct();

		// Assert
		result.Should().HaveCount(3);
		result["items:0"].Should().Be("item1");
		result["items:1"].Should().Be("item2");
		result["items:2"].Should().Be("3");
	}

	[Fact]
	public void should_roundtrip_configuration_through_struct() {
		// Arrange
		var original = new Dictionary<string, string?> {
			{ "simple", "value" },
			{ "number", "123" },
			{ "bool", "true" },
			{ "nested:key1", "nestedValue" },
			{ "nested:key2", "456" },
			{ "array:0", "first" },
			{ "array:1", "second" }
		};

		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(original)
			.Build();

		// Act
		var structValue = config.ToProtobufStruct();
		var flattened = structValue.FlattenStruct();

		// Assert
		flattened.Should().HaveCount(original.Count);
		foreach (var kvp in original) {
			flattened.Should().ContainKey(kvp.Key);
			// Note: numbers and bools are converted to their string representations
			if (kvp.Key == "number") {
				flattened[kvp.Key].Should().Be("123");
			} else if (kvp.Key == "bool") {
				flattened[kvp.Key].Should().Be("True");
			} else if (kvp.Key == "nested:key2") {
				flattened[kvp.Key].Should().Be("456");
			} else {
				flattened[kvp.Key].Should().Be(kvp.Value);
			}
		}
	}

	[Fact]
	public void should_handle_complex_nested_structure() {
		// Arrange
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "authentication:method", "Bearer" },
				{ "authentication:bearer:token", "secret" },
				{ "subscription:filter:scope", "stream" },
				{ "subscription:filter:filterType", "streamId" },
				{ "subscription:filter:expression", "test-stream" },
				{ "brokers:0", "localhost:9092" },
				{ "brokers:1", "localhost:9093" }
			})
			.Build();

		// Act
		var result = config.ToProtobufStruct();

		// Assert
		result.Fields.Should().ContainKey("authentication");
		var auth = result.Fields["authentication"].StructValue;
		auth.Fields["method"].StringValue.Should().Be("Bearer");
		auth.Fields["bearer"].StructValue.Fields["token"].StringValue.Should().Be("secret");

		result.Fields.Should().ContainKey("subscription");
		var subscription = result.Fields["subscription"].StructValue;
		subscription.Fields["filter"].StructValue.Fields["scope"].StringValue.Should().Be("stream");

		result.Fields.Should().ContainKey("brokers");
		var brokers = result.Fields["brokers"].ListValue;
		brokers.Values[0].StringValue.Should().Be("localhost:9092");
		brokers.Values[1].StringValue.Should().Be("localhost:9093");
	}

	[Theory]
	[InlineData("123", 123)]
	[InlineData("456.78", 456.78)]
	[InlineData("-42", -42)]
	public void should_parse_numbers_correctly(string input, double expected) {
		// Act
		var result = ParseValue(input);

		// Assert
		result.KindCase.Should().Be(Value.KindOneofCase.NumberValue);
		result.NumberValue.Should().Be(expected);
	}

	[Theory]
	[InlineData("true", true)]
	[InlineData("True", true)]
	[InlineData("false", false)]
	[InlineData("False", false)]
	public void should_parse_booleans_correctly(string input, bool expected) {
		// Act
		var result = ParseValue(input);

		// Assert
		result.KindCase.Should().Be(Value.KindOneofCase.BoolValue);
		result.BoolValue.Should().Be(expected);
	}

	[Theory]
	[InlineData("some text")]
	[InlineData("not-a-number")]
	[InlineData("")]
	public void should_parse_strings_correctly(string input) {
		// Act
		var result = ParseValue(input);

		// Assert
		result.KindCase.Should().Be(Value.KindOneofCase.StringValue);
		result.StringValue.Should().Be(input);
	}
}

