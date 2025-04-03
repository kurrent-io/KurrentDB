// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Index;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Index;

public class MidpointIndexCalculatorTests {
	[Fact]
	public void return_correct_next_indexes() {
		const int numIndexEntries = 100_000;
		const int numMidpoints = 1 << 10;

		var expectedMidpoints = new List<long>();
		for (var i = 0; i < numMidpoints; i++)
			expectedMidpoints.Add(PTable.GetMidpointIndex(i, numIndexEntries, numMidpoints));

		var sut = new PTable.MidpointIndexCalculator(numIndexEntries, numMidpoints);
		Assert.Equal(expectedMidpoints, ConsumeAll(sut));
	}

	[Fact]
	public void return_correct_result_when_num_midpoints_is_zero() {
		var sut = new PTable.MidpointIndexCalculator(numIndexEntries: 10, numMidpoints: 0);
		Assert.Equal([], ConsumeAll(sut));
	}

	[Fact]
	public void return_correct_result_when_num_index_entries_is_zero() {
		var sut = new PTable.MidpointIndexCalculator(numIndexEntries: 0, numMidpoints: 2);
		Assert.Equal([], ConsumeAll(sut));
	}

	[Fact]
	public void return_correct_result_when_num_index_entries_is_one() {
		var sut = new PTable.MidpointIndexCalculator(numIndexEntries: 1, numMidpoints: 2);
		Assert.Equal([0, 0], ConsumeAll(sut));
	}

	[Fact]
	public void return_correct_result_when_num_index_entries_is_large() {
		const long numIndexEntries = 46_000_000_000;
		const int numMidpoints = 1 << 28;

		var sut = new PTable.MidpointIndexCalculator(numIndexEntries, numMidpoints, numMidpoints - 2);
		Assert.Equal(
			[
				45_999_999_827,
				45_999_999_999,
			],
			ConsumeAll(sut));
	}

	static IEnumerable<long> ConsumeAll(PTable.MidpointIndexCalculator sut) {
		while (sut.NextMidpointIndex is not null) {
			yield return sut.NextMidpointIndex.Value;
			sut.Advance();
		}
	}
}
