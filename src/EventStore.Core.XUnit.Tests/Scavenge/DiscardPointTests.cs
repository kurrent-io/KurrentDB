// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class DiscardPointTests {
	[Fact]
	public void can_keep_all() {
		var sut = DiscardPoint.KeepAll;

		Assert.False(sut.ShouldDiscard(0));
		Assert.False(sut.ShouldDiscard(1));
		Assert.False(sut.ShouldDiscard(long.MaxValue));
	}

	[Fact]
	public void can_discard_before() {
		var sut = DiscardPoint.DiscardBefore(500);

		Assert.True(sut.ShouldDiscard(0));
		Assert.True(sut.ShouldDiscard(499));
		Assert.False(sut.ShouldDiscard(500));
	}

	[Fact]
	public void can_discard_including() {
		var sut = DiscardPoint.DiscardIncluding(500);

		Assert.True(sut.ShouldDiscard(0));
		Assert.True(sut.ShouldDiscard(500));
		Assert.False(sut.ShouldDiscard(501));
	}

	[Fact]
	public void can_discard_any_of() {
		var sut = DiscardPoint.DiscardBefore(50)
			.Or(DiscardPoint.DiscardBefore(500));

		Assert.True(sut.ShouldDiscard(0));
		Assert.True(sut.ShouldDiscard(499));
		Assert.False(sut.ShouldDiscard(500));
	}

	[Fact]
	public void equals() {
		Assert.Equal(DiscardPoint.DiscardBefore(3), DiscardPoint.DiscardBefore(3));
		Assert.Equal(DiscardPoint.DiscardBefore(3), DiscardPoint.DiscardIncluding(2));
		Assert.NotEqual(DiscardPoint.DiscardBefore(3), DiscardPoint.DiscardBefore(4));
	}

	[Fact]
	public void equals_operator() {
		Assert.True(DiscardPoint.DiscardBefore(3) == DiscardPoint.DiscardBefore(3));
		Assert.True(DiscardPoint.DiscardBefore(3) == DiscardPoint.DiscardIncluding(2));
		Assert.False(DiscardPoint.DiscardBefore(3) == DiscardPoint.DiscardBefore(4));
	}

	[Fact]
	public void not_equals_operator() {
		Assert.False(DiscardPoint.DiscardBefore(3) != DiscardPoint.DiscardBefore(3));
		Assert.False(DiscardPoint.DiscardBefore(3) != DiscardPoint.DiscardIncluding(2));
		Assert.True(DiscardPoint.DiscardBefore(3) != DiscardPoint.DiscardBefore(4));
	}

	[Fact]
	public void less_than_operator() {
		Assert.False(DiscardPoint.DiscardBefore(3) < DiscardPoint.DiscardBefore(3));
		Assert.False(DiscardPoint.DiscardBefore(3) < DiscardPoint.DiscardBefore(2));
		Assert.True(DiscardPoint.DiscardBefore(3) < DiscardPoint.DiscardBefore(4));
	}

	[Fact]
	public void greater_than_operator() {
		Assert.False(DiscardPoint.DiscardBefore(3) > DiscardPoint.DiscardBefore(3));
		Assert.True(DiscardPoint.DiscardBefore(3) > DiscardPoint.DiscardBefore(2));
		Assert.False(DiscardPoint.DiscardBefore(3) > DiscardPoint.DiscardBefore(4));
	}

	[Fact]
	public void get_hash_code() {
		Assert.Equal(3.GetHashCode(), DiscardPoint.DiscardBefore(3).GetHashCode());
	}
}
