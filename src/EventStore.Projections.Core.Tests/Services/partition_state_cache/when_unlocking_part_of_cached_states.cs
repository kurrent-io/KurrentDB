// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state_cache;

[TestFixture]
public class when_unlocking_part_of_cached_states {
	private PartitionStateCache _cache;
	private CheckpointTag _cachedAtCheckpointTag1;
	private CheckpointTag _cachedAtCheckpointTag2;
	private CheckpointTag _cachedAtCheckpointTag3;

	[SetUp]
	public void setup() {
		//given
		_cache = new PartitionStateCache();
		_cachedAtCheckpointTag1 = CheckpointTag.FromPosition(0, 1000, 900);
		_cachedAtCheckpointTag2 = CheckpointTag.FromPosition(0, 1200, 1100);
		_cachedAtCheckpointTag3 = CheckpointTag.FromPosition(0, 1400, 1300);
		_cache.CacheAndLockPartitionState(
			"partition1", new PartitionState("data1", null, _cachedAtCheckpointTag1), _cachedAtCheckpointTag1);
		_cache.CacheAndLockPartitionState(
			"partition2", new PartitionState("data2", null, _cachedAtCheckpointTag2), _cachedAtCheckpointTag2);
		_cache.CacheAndLockPartitionState(
			"partition3", new PartitionState("data3", null, _cachedAtCheckpointTag3), _cachedAtCheckpointTag3);
		// when
		_cache.Unlock(_cachedAtCheckpointTag2);
	}

	[Test]
	public void partitions_locked_before_the_unlock_position_cannot_be_retrieved_as_locked() {
		Assert.Throws<InvalidOperationException>(() => { _cache.GetLockedPartitionState("partition1"); });
	}

	[Test]
	public void partitions_locked_before_the_unlock_position_can_be_retrieved_and_relocked_at_later_position() {
		var data = _cache.TryGetAndLockPartitionState(
			"partition1", CheckpointTag.FromPosition(0, 1600, 1500));
		Assert.AreEqual("data1", data.State);
	}


	[Test]
	public void partitions_locked_at_the_unlock_position_cannot_be_retrieved_as_locked() {
		Assert.Throws<InvalidOperationException>(() => { _cache.GetLockedPartitionState("partition2"); });
	}

	[Test]
	public void partitions_locked_at_the_unlock_position_cannot_be_retrieved_as_relocked_at_later_position() {
		var data = _cache.TryGetAndLockPartitionState(
			"partition2", CheckpointTag.FromPosition(0, 1600, 1500));
		Assert.AreEqual("data2", data.State);
	}

	[Test]
	public void partitions_locked_after_the_unlock_position_can_be_retrieved_as_locked() {
		var data = _cache.GetLockedPartitionState("partition3");
		Assert.AreEqual("data3", data.State);
	}

	[Test]
	public void no_other_partition_states_can_be_locked_before_the_unlock_position() {
		Assert.Throws<InvalidOperationException>(() => {
			CheckpointTag at = CheckpointTag.FromPosition(0, 1040, 1030);
			_cache.CacheAndLockPartitionState("partition4", new PartitionState("data4", null, at), at);
		});
	}

	[Test]
	public void cached_partition_states_cannot_be_locked_before_the_unlock_position() {
		Assert.Throws<InvalidOperationException>(() => {
			_cache.TryGetAndLockPartitionState(
				"partition1", CheckpointTag.FromPosition(0, 1040, 1030));
		});
	}
}
