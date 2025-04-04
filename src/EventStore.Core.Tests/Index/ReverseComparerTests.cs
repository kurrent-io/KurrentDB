// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index;

[TestFixture]
public class ReverseComparerTests {
	[Test]
	public void larger_values_return_as_lower() {
		Assert.AreEqual(-1, new ReverseComparer<int>().Compare(5, 3));
	}

	[Test]
	public void smaller_values_return_as_higher() {
		Assert.AreEqual(1, new ReverseComparer<int>().Compare(3, 5));
	}

	[Test]
	public void same_values_are_equal() {
		Assert.AreEqual(0, new ReverseComparer<int>().Compare(5, 5));
	}
}
