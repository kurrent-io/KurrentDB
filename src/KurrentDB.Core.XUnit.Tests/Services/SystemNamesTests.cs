// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Services;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services;

public class SystemNamesTests {
	[Fact]
	public void IsVirtualStream_WithInMemoryStreamPrefix_ReturnsTrue()
	{
		const string streamId = SystemStreams.InMemoryStreamPrefix + "custom";
		Assert.True(SystemStreams.IsVirtualStream(streamId));

		Assert.False(SystemStreams.IsIndexStream(streamId));
	}

	[Fact]
	public void IsVirtualStream_WithPredefinedInMemoryStreams_ReturnsTrue()
	{
		Assert.True(SystemStreams.IsVirtualStream(SystemStreams.NodeStateStream));
		Assert.True(SystemStreams.IsVirtualStream(SystemStreams.GossipStream));
	}

	[Fact]
	public void IsIndexStream_WithIndexStreamPrefix_ReturnsTrue()
	{
		const string streamId = SystemStreams.IndexStreamPrefix + "custom";
		Assert.True(SystemStreams.IsIndexStream(streamId));

		Assert.False(SystemStreams.IsVirtualStream(streamId));
	}

	public static IEnumerable<object[]> NonVirtualStreams()
	{
		yield return [SystemStreams.AllStream];
		yield return [SystemStreams.EventTypesStream];
		yield return [SystemStreams.StreamsStream];
		yield return [SystemStreams.SettingsStream];
		yield return ["category-stream"];
		yield return ["regularstream"];
		yield return ["mem-withoutdollar"];
		yield return [""];
	}

	[Theory]
	[MemberData(nameof(NonVirtualStreams))]
	public void IsVirtualStream_WithNonVirtualStreams_ReturnsFalse(string streamId)
	{
		Assert.False(SystemStreams.IsVirtualStream(streamId));
	}
}
