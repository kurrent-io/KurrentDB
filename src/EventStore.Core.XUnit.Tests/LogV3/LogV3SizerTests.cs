// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.LogV3;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogV3;

public class LogV3SizerTests {
	[Fact]
	public void can_get_size() {
		var sut = new LogV3Sizer();
		Assert.Equal(4, sut.GetSizeInBytes(12345));
	}
}
