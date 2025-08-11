// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Services;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services;

public class SystemNamesTests {
	[Fact]
	public void IsInMemoryStream_WithInMemoryStreamPrefix_ReturnsTrue()
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
	public void IsInMemoryStream_WithPredefinedInMemoryStreams_ReturnsTrue()
	{
		Assert.True(SystemStreams.IsInMemoryStream(SystemStreams.NodeStateStream));
		Assert.True(SystemStreams.IsInMemoryStream(SystemStreams.GossipStream));
	}

	public static IEnumerable<object[]> NonVirtualStreams()
	{
		yield return [SystemStreams.AllStream];
		yield return [SystemStreams.EventTypesStream];
		yield return [SystemStreams.StreamsStream];
		yield return [SystemStreams.SettingsStream];
		yield return ["category-stream"];
		yield return ["regularstream"];
		yield return ["idx-withoutdollar"];
		yield return ["mem-withoutdollar"];
		yield return [""];
	}

	[Theory]
	[MemberData(nameof(NonVirtualStreams))]
	public void IsInMemoryStream_WithNonVirtualStreams_ReturnsFalse(string streamId)
	{
		Assert.False(SystemStreams.IsInMemoryStream(streamId));
	}

	[Theory]
	[MemberData(nameof(NonVirtualStreams))]
	public void IsIndexStream_WithNonVirtualStreams_ReturnsFalse(string streamId)
	{
		Assert.False(SystemStreams.IsIndexStream(streamId));
	}
}
