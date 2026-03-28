// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.LogAbstraction;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.LogAbstraction;

public class CombinedHasherTests {
	[Theory]
	[InlineData(0)]
	[InlineData(5)]
	[InlineData(uint.MaxValue)]
	[InlineData(uint.MinValue)]
	public void identity(uint x) {
		var low = (long)new IdentityLowHasher().Hash(x);
		var high = (long)new IdentityHighHasher().Hash(x);
		long actual = (high << 32) | low;
		Assert.Equal(x, actual);
	}
}
