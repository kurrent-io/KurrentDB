// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.AutoMergeLevelTests;

public class when_no_tables_are_eligible_for_manual_merge : when_max_auto_merge_level_is_set {
	public override void Setup() {
		base.Setup();
		AddTables(8);
		Assert.AreEqual(2, _result.MergedMap.InOrder().Count());

		_result = _result.MergedMap.TryManualMerge(
			_fileNameProvider,
			_ptableVersion,
			skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
	}

	[Test]
	public void should_not_manually_merge_any_table() {
		Assert.AreEqual(1, _result.MergedMap.InOrder().Count());
		AddTables(3); //adding 3 tables will cause an auto merge, but not enough to give us tables for manual merge
		Assert.AreEqual(3, _result.MergedMap.InOrder().Count());

		_result = _result.MergedMap.TryManualMerge(
			_fileNameProvider,
			_ptableVersion,
			skipIndexVerify: _skipIndexVerify);

		Assert.False(_result.HasMergedAny);
	}
}
