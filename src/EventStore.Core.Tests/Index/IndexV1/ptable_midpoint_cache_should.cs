// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index;
using NUnit.Framework;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class ptable_midpoint_cache_should : SpecificationWithDirectory {
	private static readonly ILogger Log = Serilog.Log.ForContext<ptable_midpoint_cache_should>();
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;

	public ptable_midpoint_cache_should(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	private void construct_valid_cache_for_any_combination_of_params(int maxIndexEntries) {
		var rnd = new Random(123987);
		for (int count = 0; count < maxIndexEntries; ++count) {
			for (int depth = 0; depth < 15; ++depth) {
				PTable ptable = null;
				try {
					Log.Verbose("Creating PTable with count {0}, depth {1}", count, depth);
					ptable = ConstructPTable(
						GetFilePathFor(string.Format("{0}-{1}-indexv{2}.ptable", count, depth, _ptableVersion)),
						count, rnd, depth);
					ValidateCache(ptable.GetMidPoints(), count, depth);
				} finally {
					if (ptable != null) {
						ptable.Dispose();
					}
				}
			}
		}
	}

	private PTable ConstructPTable(string file, int count, Random rnd, int depth) {
		var memTable = new HashListMemTable(_ptableVersion, 20000);
		for (int i = 0; i < count; ++i) {
			memTable.Add((uint)rnd.Next(), rnd.Next(0, 1 << 20), Math.Abs(rnd.Next() * rnd.Next()));
		}

		var ptable = PTable.FromMemtable(memTable, file, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify: _skipIndexVerify);
		return ptable;
	}

	private void ValidateCache(ReadOnlySpan<PTable.Midpoint> cache, int count, int depth) {
		if (count == 0 || depth == 0) {
			Assert.True(cache.IsEmpty);
			return;
		}

		if (count == 1) {
			Assert.False(cache.IsEmpty);
			Assert.AreEqual(2, cache.Length);
			Assert.AreEqual(0, cache[0].ItemIndex);
			Assert.AreEqual(0, cache[1].ItemIndex);
			return;
		}

		Assert.False(cache.IsEmpty);
		Assert.AreEqual(Math.Min(count, 1 << depth), cache.Length);

		Assert.AreEqual(0, cache[0].ItemIndex);
		for (int i = 1; i < cache.Length; ++i) {
			Assert.IsTrue(cache[i - 1].Key.GreaterEqualsThan(cache[i].Key), "Expected {0} to be >= {1}",
				cache[i - 1].Key, cache[i].Key);
			Assert.Less(cache[i - 1].ItemIndex, cache[i].ItemIndex);
		}

		Assert.AreEqual(count - 1, cache[cache.Length - 1].ItemIndex);
	}

	[Test, Category("LongRunning"), Ignore("Veerrrryyy long running :)")]
	public void construct_valid_cache_for_any_combination_of_params_large() {
		construct_valid_cache_for_any_combination_of_params(4096);
	}

	[Test]
	public void construct_valid_cache_for_any_combination_of_params_small() {
		construct_valid_cache_for_any_combination_of_params(20);
	}
}
