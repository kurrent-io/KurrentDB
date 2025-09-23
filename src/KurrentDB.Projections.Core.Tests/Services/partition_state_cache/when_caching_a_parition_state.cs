// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.partition_state_cache;

[TestFixture]
public class when_caching_a_parition_state {
	private PartitionStateCache _cache;
	private CheckpointTag _cachedAtCheckpointTag;

	[SetUp]
	public void when() {
		_cache = new PartitionStateCache();
		_cachedAtCheckpointTag = CheckpointTag.FromPosition(0, 1000, 900);
		_cache.CachePartitionState("partition", new("data", null, _cachedAtCheckpointTag));
	}

	[Test]
	public void the_state_cannot_be_retrieved_as_locked() {
		Assert.Throws<InvalidOperationException>(() => {
			var state = _cache.GetLockedPartitionState("partition");
			Assert.AreEqual("data", state.State);
		});
	}

	[Test]
	public void the_state_can_be_retrieved() {
		var state = _cache.TryGetPartitionState("partition");
		Assert.AreEqual("data", state.State);
	}

	[Test]
	public void the_state_can_be_retrieved_as_unlocked_and_relocked_at_later_position() {
		var state = _cache.TryGetAndLockPartitionState("partition", CheckpointTag.FromPosition(0, 1500, 1400));
		Assert.AreEqual("data", state.State);
	}
}
