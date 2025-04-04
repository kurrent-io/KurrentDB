// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.LogAbstraction;
using Xunit;
using LogV3StreamId = System.UInt32;

namespace EventStore.Core.XUnit.Tests.LogAbstraction;

public class CombinedHasherTests {
	[Theory]
	[InlineData(0)]
	[InlineData(5)]
	[InlineData(LogV3StreamId.MaxValue)]
	[InlineData(LogV3StreamId.MinValue)]
	public void identity(LogV3StreamId x) {
		var low = (long)new IdentityLowHasher().Hash(x);
		var high = (long)new IdentityHighHasher().Hash(x);
		long actual = (high << 32) | low;
		Assert.Equal(x, actual);
	}
}
