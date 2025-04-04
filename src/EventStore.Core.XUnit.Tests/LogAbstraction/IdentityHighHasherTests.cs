// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.LogAbstraction;
using Xunit;
using LogV3StreamId = System.UInt32;

namespace EventStore.Core.XUnit.Tests.LogAbstraction;

public class IdentityHighHasherTests {
	[Theory]
	[InlineData(0, 0)]
	[InlineData(5, 0)]
	[InlineData(0xAAAA_BBBB, 0)]
	[InlineData(0xFFFF_FFFF, 0)]
	public void hashes_correctly(LogV3StreamId x, uint expected) {
		var sut = new IdentityHighHasher();
		Assert.Equal(expected, sut.Hash(x));
	}
}
