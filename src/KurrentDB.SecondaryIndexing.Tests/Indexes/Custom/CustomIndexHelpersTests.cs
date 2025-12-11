// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Indexes.Custom;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.Custom;

public class CustomIndexHelpersTests {
	[Theory]
	[InlineData("my-index", null, "$idx-my-index")]
	[InlineData("my-index", "country", "$idx-my-index:country")]
	public void can_get_query_stream_name(string inputIndexName, string? intputField, string expectedStreamName) {
		var actualStreamName = CustomIndexHelpers.GetQueryStreamName(inputIndexName, intputField);
		Assert.Equal(expectedStreamName, actualStreamName);
	}

	[Theory]
	[InlineData("$idx-my-index", "my-index", null)]
	[InlineData("$idx-my-index:country", "my-index", "country")]
	public void can_parse_query_stream_name(string input, string expectedIndexName, string? expectedField) {
		CustomIndexHelpers.ParseQueryStreamName(input, out var actualIndexName, out var actualField);
		Assert.Equal(expectedIndexName, actualIndexName);
		Assert.Equal(expectedField, actualField);
	}

	[Theory]
	[InlineData("my-index", "$CustomIndex-my-index")]
	public void can_get_management_stream_name(string input, string expectedStreamName) {
		Assert.Equal(expectedStreamName, CustomIndexHelpers.GetManagementStreamName(input));
	}

	[Theory]
	[InlineData("$CustomIndex-my-index", "my-index")]
	public void can_parse_management_stream_name(string input, string expectedIndexName) {
		Assert.Equal(expectedIndexName, CustomIndexHelpers.ParseManagementStreamName(input));
	}
}
