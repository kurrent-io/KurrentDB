// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Time;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class InstantTests {
	[Fact]
	public void can_measure_elapsed_seconds() {
		var x = Instant.FromSeconds(4);
		var y = Instant.FromSeconds(6);
		Assert.Equal(2, y.ElapsedSecondsSince(x));
	}

	[Theory]
	[InlineData(4, 4, 0)]
	[InlineData(4, 6, 2_000_000)]
	[InlineData(123, 1_000_000_000, 999_999_877_000_000)]
	public void can_measure_elapsed_time(int startSecs, int endSecs, long elapsedMicroseconds) {
		var x = Instant.FromSeconds(startSecs);
		var y = Instant.FromSeconds(endSecs);
		Assert.Equal(elapsedMicroseconds, y.ElapsedTimeSince(x).TotalMicroseconds);
	}

	[Fact]
	public void rounds_up_elapsed_time() {
		var x = Instant.Now;
		var y = new Instant(x.Ticks + 1);
		Assert.True(y.ElapsedTimeSince(x).Ticks > 0);
	}

	[Fact]
	public void add() {
		Assert.Equal(
			Instant.FromSeconds(4),
			Instant.FromSeconds(3).Add(TimeSpan.FromSeconds(1)));
	}

	[Fact]
	public void equal_to() {
		Assert.False(Instant.FromSeconds(3) == Instant.FromSeconds(4));
		Assert.True(Instant.FromSeconds(3) == Instant.FromSeconds(3));
	}

	[Fact]
	public void not_equal_to() {
		Assert.True(Instant.FromSeconds(3) != Instant.FromSeconds(4));
		Assert.False(Instant.FromSeconds(3) != Instant.FromSeconds(3));
	}

	[Fact]
	public void less_than() {
		Assert.True(Instant.FromSeconds(3) < Instant.FromSeconds(4));
		Assert.False(Instant.FromSeconds(3) < Instant.FromSeconds(3));
		Assert.False(Instant.FromSeconds(3) < Instant.FromSeconds(2));
	}

	[Fact]
	public void greater_than() {
		Assert.False(Instant.FromSeconds(3) > Instant.FromSeconds(4));
		Assert.False(Instant.FromSeconds(3) > Instant.FromSeconds(3));
		Assert.True(Instant.FromSeconds(3) > Instant.FromSeconds(2));
	}

	[Fact]
	public void less_than_or_equal_to() {
		Assert.True(Instant.FromSeconds(3) <= Instant.FromSeconds(4));
		Assert.True(Instant.FromSeconds(3) <= Instant.FromSeconds(3));
		Assert.False(Instant.FromSeconds(3) <= Instant.FromSeconds(2));
	}

	[Fact]
	public void greater_than_or_equal_to() {
		Assert.False(Instant.FromSeconds(3) >= Instant.FromSeconds(4));
		Assert.True(Instant.FromSeconds(3) >= Instant.FromSeconds(3));
		Assert.True(Instant.FromSeconds(3) >= Instant.FromSeconds(2));
	}
}
