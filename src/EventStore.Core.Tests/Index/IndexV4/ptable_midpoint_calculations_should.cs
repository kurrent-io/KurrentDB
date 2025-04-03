// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Numerics;
using EventStore.Core.Index;
using NUnit.Framework;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Tests.Index.IndexV4;

public class ptable_midpoint_calculations_should : SpecificationWithDirectory {
	protected byte _ptableVersion = PTableVersions.IndexV4;
	private static readonly ILogger Log = Serilog.Log.ForContext<ptable_midpoint_calculations_should>();

	private void construct_same_midpoint_indexes_for_any_combination_of_params(int maxIndexEntries) {
		for (var numIndexEntries = 0; numIndexEntries < maxIndexEntries; numIndexEntries++) {
			for (var depth = 0; depth < 20; depth++) {
				var requiredMidpointsCount =
					PTable.GetRequiredMidpointCountCached(numIndexEntries, _ptableVersion, depth);
				List<long> requiredMidpoints = new List<long>();
				for (var k = 0; k < requiredMidpointsCount; k++) {
					var index = PTable.GetMidpointIndex(k, numIndexEntries, requiredMidpointsCount);
					requiredMidpoints.Add(index);
				}

				List<long> calculatedMidpoints = new List<long>();
				for (var k = 0; k < numIndexEntries; k++) {
					if (Utils.IsMidpointIndex(k, numIndexEntries, requiredMidpointsCount)) {
						calculatedMidpoints.Add(k);
					}
				}

				if (numIndexEntries == 1 && calculatedMidpoints.Count == 1) {
					calculatedMidpoints.Add(calculatedMidpoints[0]);
				}

				if (requiredMidpoints.Count != calculatedMidpoints.Count) {
					Log.Error(
						"Midpoint count mismatch for numIndexEntries: {0}, depth:{1} - Expected {2}, Found {3}",
						numIndexEntries, depth, requiredMidpoints.Count, calculatedMidpoints.Count);
				}

				Assert.AreEqual(requiredMidpoints.Count, calculatedMidpoints.Count);

				for (var i = 0; i < requiredMidpoints.Count; i++) {
					if (requiredMidpoints[i] != calculatedMidpoints[i]) {
						Log.Error(
							"Midpoint mismatch at index {0} for numIndexEntries: {1}, depth:{2} - Expected {3}, Found {4}",
							i, numIndexEntries, depth, requiredMidpoints[i], calculatedMidpoints[i]);
					}

					Assert.AreEqual(requiredMidpoints[i], calculatedMidpoints[i]);
				}
			}
		}
	}

	[Test, Category("LongRunning"), Ignore("Long running")]
	public void construct_same_midpoint_indexes_for_any_combination_of_params_large() {
		construct_same_midpoint_indexes_for_any_combination_of_params(4096);
	}

	[Test]
	public void construct_same_midpoint_indexes_for_any_combination_of_params_small() {
		construct_same_midpoint_indexes_for_any_combination_of_params(200);
	}

	[Test]
	public void return_a_positive_index_even_for_really_big_ptables() {
		var depth = 28;
		var index = PTable.GetMidpointIndex(
			k: 265_000_000,
			numIndexEntries: 46_000_000_000,
			numMidpoints: 1 << depth);

		Assert.Positive(index);
	}

	[Test]
	public void return_correct_indexes_for_really_big_ptables() {
		var numIndexEntries = 46_000_000_000;
		var numMidpoints = 1 << 28;

		for (var k = numMidpoints - 1000; k < numMidpoints; k++) {
			var index = PTable.GetMidpointIndex(k, numIndexEntries, numMidpoints);
			var correctIndex = GetMidpointIndexBigInt(k, numIndexEntries, numMidpoints);
			Assert.Positive(correctIndex);
			Assert.AreEqual(correctIndex, index);
		}
	}

	private static long GetMidpointIndexBigInt(int k, long numIndexEntries, int numMidpoints) {
		if (k == 0)
			return 0;

		if (k == numMidpoints - 1)
			return numIndexEntries - 1;

		var res = (BigInteger)k * (numIndexEntries - 1) / (numMidpoints - 1);
		return (long)res;
	}
}
