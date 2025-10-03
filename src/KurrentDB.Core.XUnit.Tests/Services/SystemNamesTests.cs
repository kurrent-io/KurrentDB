// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services;

public class SystemNamesTests {
	[Fact]
	public void IsMemoryStream_WithInMemoryStreamPrefix_ReturnsTrue()
	{
		const string streamId = SystemStreams.InMemoryStreamPrefix + "custom";

		Assert.True(SystemStreams.IsInMemoryStream(streamId));
		Assert.False(SystemStreams.IsIndexStream(streamId));
	}

	[Fact]
	public void IsIndexStream_WithIndexStreamPrefix_ReturnsTrue()
	{
		const string streamId = SystemStreams.IndexStreamPrefix + "custom";

		Assert.True(SystemStreams.IsIndexStream(streamId));
		Assert.False(SystemStreams.IsInMemoryStream(streamId));
	}

	[Fact]
	public void IsVirtualStream_WithPredefinedVirtualStreams_ReturnsTrue()
	{
		Assert.True(SystemStreams.IsInMemoryStream(SystemStreams.NodeStateStream));
		Assert.True(SystemStreams.IsInMemoryStream(SystemStreams.GossipStream));
	}


	[Theory]
	[InlineData(SystemStreams.AllStream)]
	[InlineData(SystemStreams.EventTypesStream)]
	[InlineData(SystemStreams.StreamsStream)]
	[InlineData(SystemStreams.SettingsStream)]
	[InlineData("caregory-stream")]
	[InlineData("regularstream")]
	[InlineData("idx-withoutdollar")]
	[InlineData("mem-withoutdollar")]
	[InlineData("")]
	public void IsVirtualStream_WithoutMemOrIdxPrefix_ReturnsFalse(string streamId)
	{
		Assert.False(SystemStreams.IsInMemoryStream(streamId));
		Assert.False(SystemStreams.IsIndexStream(streamId));
	}
}
