// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Diagnostics;

namespace EventStore.Plugins.Tests.Diagnostics;

public class PluginDiagnosticsDataTests {
	[Theory]
	[InlineData(true)]
	[InlineData(23)]
	[InlineData(99L)]
	[InlineData(2.23)]
	[InlineData("some-value")]
	public void can_get_value_from_data(dynamic expectedValue) =>
		gets_value_from_data(expectedValue);
	
	[Theory]
	[InlineData(true)]
	[InlineData(23)]
	[InlineData(99L)]
	[InlineData(2.23)]
	[InlineData("some-value")]
	public void can_get_default_value_from_data(dynamic expectedValue) =>
		gets_default_value_from_data(expectedValue);
	
	static void gets_value_from_data<T>(T expectedValue) {
		var sut = new PluginDiagnosticsData {
			Data = new() { ["value"] = expectedValue }
		};

		sut.GetValue<T>("value").Should()
			.BeEquivalentTo(expectedValue, $"because the retrieved value should be {typeof(T).Name}");
	}

	static void gets_default_value_from_data<T>(T expectedValue) {
		var sut = new PluginDiagnosticsData {
			Data = new()
		};

		sut.GetValue("missing-value", defaultValue: expectedValue)
			.Should().BeEquivalentTo(expectedValue, $"because the retrieved value should be {typeof(T).Name}");
	}
}
