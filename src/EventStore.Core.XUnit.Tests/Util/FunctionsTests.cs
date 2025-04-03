// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Util;
using EventStore.Core.XUnit.Tests.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Util;

public class FunctionsTests {
	[Fact]
	public void sanity_check() {
		var clock = new FakeClock();
		clock.SecondsSinceEpoch = 12345;
		var calls = 0;
		var f = () => ++calls;
		var debounced = f.Debounce(TimeSpan.FromSeconds(5), clock);

		// no calls to begin with
		Assert.Equal(0, calls);

		// calls in the first place
		Assert.Equal(1, debounced());


		// cached for second call
		Assert.Equal(1, debounced());

		// still cached after some time has passed
		clock.AdvanceSeconds(3);
		Assert.Equal(1, debounced());

		// called again after more time has passed.
		clock.AdvanceSeconds(3);
		Assert.Equal(2, debounced());
	}
}
