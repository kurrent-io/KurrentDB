// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.User;

public class IndexHelpersTests {
	[Theory]
	[InlineData("my-index", null, "$idx-user-my-index")]
	[InlineData("my-index", "country", "$idx-user-my-index:country")]
	public void can_get_query_stream_name(string inputIndexName, string? intputField, string expectedStreamName) {
		var actualStreamName = UserIndexHelpers.GetQueryStreamName(inputIndexName, intputField);
		Assert.Equal(expectedStreamName, actualStreamName);
	}

	[Theory]
	[InlineData("$idx-user-my-index", "my-index", null)]
	[InlineData("$idx-user-my-index:country", "my-index", "country")]
	public void can_parse_query_stream_name(string input, string expectedIndexName, string? expectedField) {
		UserIndexHelpers.ParseQueryStreamName(input, out var actualIndexName, out var actualField);
		Assert.Equal(expectedIndexName, actualIndexName);
		Assert.Equal(expectedField, actualField);
	}

	[Theory]
	[InlineData("my-index", "$UserIndex-my-index")]
	public void can_get_management_stream_name(string input, string expectedStreamName) {
		Assert.Equal(expectedStreamName, UserIndexHelpers.GetManagementStreamName(input));
	}
}
