// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Components.Streams;
using Xunit;

namespace KurrentDB.Components.Tests;

public class EventFormattingTests {
	[Fact]
	public void Pretty_prints_valid_json() {
		var formatted = EventFormatting.FormatJson(Encoding.UTF8.GetBytes("""{"a":1,"b":"x"}"""));

		// Indented output spans multiple lines and preserves the values.
		Assert.Contains("\n", formatted);
		Assert.Contains("\"a\": 1", formatted);
		Assert.Contains("\"b\": \"x\"", formatted);
	}

	[Fact]
	public void Falls_back_to_raw_utf8_for_non_json() {
		Assert.Equal("not json", EventFormatting.FormatJson(Encoding.UTF8.GetBytes("not json")));
	}

	[Fact]
	public void Empty_input_falls_back_to_empty_string() {
		Assert.Equal("", EventFormatting.FormatJson(ReadOnlyMemory<byte>.Empty));
	}

	[Fact]
	public void Truncates_long_non_json_to_a_preview() {
		var formatted = EventFormatting.FormatJson(Encoding.UTF8.GetBytes(new string('x', 5000)));

		Assert.Equal(new string('x', 200) + "...", formatted);
	}

	[Fact]
	public void Does_not_truncate_short_non_json() {
		var text = new string('x', 200);

		Assert.Equal(text, EventFormatting.FormatJson(Encoding.UTF8.GetBytes(text)));
	}
}
