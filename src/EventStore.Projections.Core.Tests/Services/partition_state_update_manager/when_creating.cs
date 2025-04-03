// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state_update_manager;

[TestFixture]
public class when_creating {
	[Test]
	public void no_exceptions_are_thrown() {
		new PartitionStateUpdateManager(ProjectionNamesBuilder.CreateForTest("projection"));
	}

	[Test]
	public void null_naming_builder_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => { new PartitionStateUpdateManager(null); });
	}
}
