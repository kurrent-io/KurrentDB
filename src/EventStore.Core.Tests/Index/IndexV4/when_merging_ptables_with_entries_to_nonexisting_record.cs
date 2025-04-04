// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV4;

[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class
	when_merging_ptables_with_entries_to_nonexisting_record : IndexV1.
		when_merging_ptables_with_entries_to_nonexisting_record_in_newer_index_versions {
	public when_merging_ptables_with_entries_to_nonexisting_record(byte version, bool skipIndexVerify) : base(
		version, skipIndexVerify) {
	}

	[Test]
	public void the_correct_midpoints_are_cached() {
		var midpoints = _newtable.GetMidPoints();
		var requiredMidpoints = PTable.GetRequiredMidpointCountCached(_newtable.Count, _ptableVersion);

		Assert.AreEqual(requiredMidpoints, midpoints.Length);

		var position = 0;
		foreach (var item in _newtable.IterateAllInOrder()) {
			if (Utils.IsMidpointIndex(position, _newtable.Count, requiredMidpoints)) {
				Assert.AreEqual(item.Stream, midpoints[position].Key.Stream);
				Assert.AreEqual(item.Version, midpoints[position].Key.Version);
				Assert.AreEqual(position, midpoints[position].ItemIndex);
				position++;
			}
		}
	}
}
