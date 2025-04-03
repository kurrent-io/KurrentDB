// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state_cache;

[TestFixture]
public class when_the_partition_state_cache_has_been_created {
	private PartitionStateCache _cache;
	private Exception _exception;

	[SetUp]
	public void when() {
		try {
			_cache = new PartitionStateCache();
		} catch (Exception ex) {
			_exception = ex;
		}
	}

	[Test]
	public void it_has_been_created() {
		Assert.IsNotNull(_cache, ((object)_exception ?? "").ToString());
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
