// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexVAny;


[TestFixture]
public class midpoint_assertions {
	private const long MAX_INDEX_ENTRIES = 1000000000000L;
	private const int MAX_TEST_DEPTH = 35;
	private const int MAX_DEPTH = 28;
	private static readonly byte[] IndexVersions = { PTableVersions.IndexV1, PTableVersions.IndexV2, PTableVersions.IndexV3, PTableVersions.IndexV4 };

	private static int getIncrement(long numIndexEntries) {
		if (numIndexEntries < 100)
			return 1;
		if (numIndexEntries < 1000000)
			return 1337;
		return 133333337;
	}

	public class TestCases : IEnumerable {
		public IEnumerator GetEnumerator() {
			foreach (var version in IndexVersions) {
				for (int minDepth = 0; minDepth <= MAX_TEST_DEPTH; minDepth++) {
					yield return new object[] { version, minDepth };
				}
			}
		}
	}

	[Test, TestCaseSource(typeof(TestCases))]
	public void file_size_should_be_greater_or_equal_after_adding_midpoints(byte version, int minDepth) {
		for (long numIndexEntries = 0; numIndexEntries < MAX_INDEX_ENTRIES; numIndexEntries += getIncrement(numIndexEntries)) {
			string testCase = $"numIndexEntries: {numIndexEntries}, minDepth: {minDepth}, version: {version}";
			var midpointCount = PTable.GetRequiredMidpointCountCached(numIndexEntries, version, minDepth);
			if (version < PTableVersions.IndexV4) {
				Assert.AreEqual(0, midpointCount, testCase);
			} else {
				Assert.GreaterOrEqual(midpointCount, Math.Min(1 << Math.Min(MAX_DEPTH, minDepth), numIndexEntries), testCase);
				Assert.LessOrEqual(midpointCount, Math.Max(2, numIndexEntries), testCase);
			}

			long fileSizeUpToIndexEntries = PTable.GetFileSizeUpToIndexEntries(numIndexEntries, version);
			long fileSizeUpToMidpointEntries =
				PTable.GetFileSizeUpToMidpointEntries(fileSizeUpToIndexEntries, midpointCount, version);

			if (version < PTableVersions.IndexV4) {
				Assert.AreEqual(fileSizeUpToIndexEntries, fileSizeUpToMidpointEntries, testCase);
			} else {
				Assert.GreaterOrEqual(fileSizeUpToMidpointEntries, fileSizeUpToIndexEntries, testCase);
			}
		}
	}

	[Test]
	public void for_a_fixed_min_depth_and_increasing_index_entries_midpoint_count_should_increase_monotonically() {
		for (int minDepth = 0; minDepth <= MAX_TEST_DEPTH; minDepth++) {
			long prevMidpointCount = 0L;
			for (long numIndexEntries = 0; numIndexEntries < MAX_INDEX_ENTRIES; numIndexEntries += getIncrement(numIndexEntries)) {
				string testCase = $"numIndexEntries: {numIndexEntries}, minDepth: {minDepth}";
				var midpointCount = PTable.GetRequiredMidpointCountCached(numIndexEntries, PTableVersions.IndexV4, minDepth);
				Assert.GreaterOrEqual(midpointCount, prevMidpointCount, testCase);
				Assert.GreaterOrEqual(midpointCount, Math.Min(1 << Math.Min(MAX_DEPTH, minDepth), numIndexEntries), testCase);
				Assert.LessOrEqual(midpointCount, Math.Max(2, numIndexEntries), testCase);
				prevMidpointCount = midpointCount;
			}
		}
	}

	[Test]
	public void for_a_fixed_number_of_index_entries_and_increasing_min_depth_midpoint_count_should_increase_monotonically() {
		for (long numIndexEntries = 0; numIndexEntries < MAX_INDEX_ENTRIES; numIndexEntries += getIncrement(numIndexEntries)) {
			long prevMidpointCount = 0L;
			for (int minDepth = 0; minDepth <= MAX_TEST_DEPTH; minDepth++) {
				string testCase = $"numIndexEntries: {numIndexEntries}, minDepth: {minDepth}";
				var midpointCount = PTable.GetRequiredMidpointCountCached(numIndexEntries, PTableVersions.IndexV4, minDepth);
				Assert.GreaterOrEqual(midpointCount, prevMidpointCount, testCase);
				Assert.GreaterOrEqual(midpointCount, Math.Min(1 << Math.Min(MAX_DEPTH, minDepth), numIndexEntries), testCase);
				Assert.LessOrEqual(midpointCount, Math.Max(2, numIndexEntries), testCase);
				prevMidpointCount = midpointCount;
			}
		}
	}
}
