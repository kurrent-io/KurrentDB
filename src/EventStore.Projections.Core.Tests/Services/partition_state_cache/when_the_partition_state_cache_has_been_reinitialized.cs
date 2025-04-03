// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state_cache;

[TestFixture]
public class when_the_partition_state_cache_has_been_reinitialized {
	private PartitionStateCache _cache;
	private CheckpointTag _cachedAtCheckpointTag1;
	private CheckpointTag _cachedAtCheckpointTag2;
	private CheckpointTag _cachedAtCheckpointTag3;

	[SetUp]
	public void setup() {
		//given
		_cache = new PartitionStateCache(10);
		_cachedAtCheckpointTag1 = CheckpointTag.FromPosition(0, 1000, 900);
		for (var i = 0; i < 15; i++) {
			CheckpointTag at = CheckpointTag.FromPosition(0, 1000 + (i * 100), 1000 + (i * 100) - 50);
			_cache.CacheAndLockPartitionState("partition1", new PartitionState("data1", null, at), at);
		}

		_cachedAtCheckpointTag2 = CheckpointTag.FromPosition(0, 20100, 20050);
		_cachedAtCheckpointTag3 = CheckpointTag.FromPosition(0, 20200, 20150);
		_cache.CacheAndLockPartitionState(
			"partition1", new PartitionState("data1", null, _cachedAtCheckpointTag1), _cachedAtCheckpointTag1);
		_cache.CacheAndLockPartitionState(
			"partition2", new PartitionState("data2", null, _cachedAtCheckpointTag2), _cachedAtCheckpointTag2);
		_cache.CacheAndLockPartitionState(
			"partition3", new PartitionState("data3", null, _cachedAtCheckpointTag3), _cachedAtCheckpointTag3);
		_cache.Unlock(_cachedAtCheckpointTag2);
		// when
		_cache.Initialize();
	}

	[Test]
	public void state_can_be_cached() {
		CheckpointTag at = CheckpointTag.FromPosition(0, 100, 90);
		_cache.CacheAndLockPartitionState("partition", new PartitionState("data", null, at), at);
	}

	[Test]
	public void no_items_are_cached() {
		Assert.AreEqual(0, _cache.CachedItemCount);
	}

	[Test]
	public void random_item_cannot_be_retrieved_as_locked() {
		Assert.IsNull(
			_cache.TryGetAndLockPartitionState(
				"random", CheckpointTag.FromPosition(0, 200, 190)),
			"Cache should be empty");
	}

	[Test]
	public void random_item_cannot_be_retrieved() {
		Assert.IsNull(_cache.TryGetPartitionState("random"), "Cache should be empty");
	}

	[Test]
	public void root_partition_state_cannot_be_retrieved() {
		Assert.IsNull(
			_cache.TryGetAndLockPartitionState(
				"", CheckpointTag.FromPosition(0, 200, 190)),
			"Cache should be empty");
	}

	[Test]
	public void unlock_succeeds() {
		_cache.Unlock(CheckpointTag.FromPosition(0, 300, 290));
	}
}
