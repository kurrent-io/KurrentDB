// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.partition_state_update_manager;

[TestFixture]
public class when_creating {
	[Test]
	public void no_exceptions_are_thrown() {
		_ = new PartitionStateUpdateManager(ProjectionNamesBuilder.CreateForTest("projection"));
	}

	[Test]
	public void null_naming_builder_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => { _ = new PartitionStateUpdateManager(null); });
	}
}
