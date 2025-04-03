// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state_cache;

[TestFixture]
public class when_relocking_the_state_at_the_same_position {
	private PartitionStateCache _cache;
	private CheckpointTag _cachedAtCheckpointTag;

	[SetUp]
	public void given() {
		//given
		_cache = new PartitionStateCache();
		_cachedAtCheckpointTag = CheckpointTag.FromPosition(0, 1000, 900);
		_cache.CacheAndLockPartitionState("partition", new PartitionState("data", null, _cachedAtCheckpointTag),
			_cachedAtCheckpointTag);
	}

	[Test]
	public void thorws_invalid_operation_exception_if_not_allowed() {
		Assert.Throws<InvalidOperationException>(() => {
			_cache.TryGetAndLockPartitionState("partition", CheckpointTag.FromPosition(0, 1000, 900));
		});
	}

	[Test]
	public void the_state_can_be_retrieved() {
		var state = _cache.TryGetPartitionState("partition");
		Assert.AreEqual("data", state.State);
	}
}
