// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.LogAbstraction;
using Xunit;
using LogV3StreamId = System.UInt32;

namespace EventStore.Core.XUnit.Tests.LogAbstraction;

public class IdentityLowHasherTests {
	[Theory]
	[InlineData(0, 0)]
	[InlineData(5, 5)]
	[InlineData(0xAAAA_BBBB, 0xAAAA_BBBB)]
	[InlineData(0xFFFF_FFFF, 0xFFFF_FFFF)]
	public void hashes_correctly(LogV3StreamId x, uint expected) {
		var sut = new IdentityLowHasher();
		Assert.Equal(expected, sut.Hash(x));
	}
}
