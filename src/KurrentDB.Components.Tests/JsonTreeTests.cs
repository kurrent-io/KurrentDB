// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Bunit;
using KurrentDB.Components.Tools;
using Xunit;

namespace KurrentDB.Components.Tests;

// JsonTree parses a JSON string and renders an interactive, theme-aware tree (JsonNode is the recursive
// node). It replaced the vendored @alenaksu/json-viewer web component, so these lock in the behaviour:
// render object/array contents, fall back gracefully on non-JSON, lazily expand nested nodes, and tag
// primitives with type classes for colouring. No DI/Mud needed — a plain bUnit context renders them.
public class JsonTreeTests {
	static IRenderedComponent<JsonTree> Render(BunitContext ctx, string json) =>
		ctx.Render<JsonTree>(p => p.Add(c => c.Json, json));

	[Fact]
	public async Task Renders_object_keys_and_values() {
		await using var ctx = new BunitContext();

		var cut = Render(ctx, """{"name":"bob","count":42}""");

		Assert.NotEmpty(cut.FindAll(".json-tree"));
		Assert.Contains("name", cut.Markup);
		Assert.Contains("bob", cut.Markup);
		Assert.Contains("count", cut.Markup);
		Assert.Contains("42", cut.Markup);
	}

	[Fact]
	public async Task Invalid_json_falls_back_to_raw_text() {
		await using var ctx = new BunitContext();

		var cut = Render(ctx, "this is not json");

		Assert.Empty(cut.FindAll(".json-tree"));
		Assert.Contains("this is not json", cut.Find("pre.json-raw").TextContent);
	}

	[Fact]
	public async Task Nested_value_is_collapsed_until_expanded() {
		await using var ctx = new BunitContext();

		var cut = Render(ctx, """{"outer":{"inner":123}}""");

		// Root is expanded, so "outer" shows — but its child is lazy, so "inner" isn't rendered yet.
		Assert.Contains("outer", cut.Markup);
		Assert.DoesNotContain("inner", cut.Markup);

		// Toggle order is document order: [0] = root, [1] = the collapsed "outer" node.
		cut.FindAll(".json-toggle")[1].Click();

		Assert.Contains("inner", cut.Markup);
		Assert.Contains("123", cut.Markup);
	}

	[Fact]
	public async Task Primitives_get_typed_css_classes() {
		await using var ctx = new BunitContext();

		var cut = Render(ctx, """{"s":"x","n":1,"b":true,"z":null}""");

		Assert.NotEmpty(cut.FindAll(".json-string"));
		Assert.NotEmpty(cut.FindAll(".json-number"));
		Assert.NotEmpty(cut.FindAll(".json-bool"));
		Assert.NotEmpty(cut.FindAll(".json-null"));
	}

	[Fact]
	public async Task Array_items_are_indexed() {
		await using var ctx = new BunitContext();

		var cut = Render(ctx, """["a","b","c"]""");

		// Array items are keyed by index, so there's a json-key per element.
		Assert.Equal(3, cut.FindAll(".json-key").Count);
		Assert.Contains("\"a\"", cut.Markup);
		Assert.Contains("\"c\"", cut.Markup);
	}
}
