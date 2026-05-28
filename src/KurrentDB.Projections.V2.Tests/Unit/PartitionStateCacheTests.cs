// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using KurrentDB.Projections.Core.Services.Processing.V2;

namespace KurrentDB.Projections.V2.Tests.Unit;

public class PartitionStateCacheTests {
	[Test]
	public async Task try_get_returns_false_for_missing_key() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		var hit = cache.TryGet("missing", out var value);

		await Assert.That(hit).IsFalse();
		await Assert.That(value).IsNull();
	}

	[Test]
	public async Task set_then_try_get_returns_value() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		await cache.Set("k", "v1", CancellationToken.None);

		var hit = cache.TryGet("k", out var value);
		await Assert.That(hit).IsTrue();
		await Assert.That(value).IsEqualTo("v1");
	}

	[Test]
	public async Task set_overwrites_existing_value() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		await cache.Set("k", "v1", CancellationToken.None);
		await cache.Set("k", "v2", CancellationToken.None);

		cache.TryGet("k", out var value);
		await Assert.That(value).IsEqualTo("v2");
		await Assert.That(cache.Count).IsEqualTo(1L);
	}

	[Test]
	public async Task null_value_is_preserved() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		await cache.Set("k", null, CancellationToken.None);

		var hit = cache.TryGet("k", out var value);
		await Assert.That(hit).IsTrue();
		await Assert.That(value).IsNull();
	}

	[Test]
	public async Task exceeding_capacity_evicts_and_counter_increments() {
		const int capacity = 4;
		// SIEVE eviction is asynchronous (runs on a spawned background thread), so we insert
		// well beyond capacity, then poll until evictions fire or a deadline is reached.
		await using var cache = new PartitionStateCache(capacity, name: "test", projectionName: "p");

		for (var i = 0; i < capacity * 10; i++)
			await cache.Set($"k{i}", $"v{i}", CancellationToken.None);

		// Wait up to 5s for the background eviction task to fire.
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		while (cache.Evictions == 0 && !cts.Token.IsCancellationRequested)
			await Task.Delay(10, CancellationToken.None);

		await Assert.That(cache.Evictions).IsGreaterThanOrEqualTo(1L);
	}

	[Test]
	public async Task concurrent_reads_and_writes_do_not_throw() {
		await using var cache = new PartitionStateCache(capacity: 64, name: "test", projectionName: "p");

		var writers = Enumerable.Range(0, 8).Select(w => Task.Run(async () => {
			for (var i = 0; i < 200; i++)
				await cache.Set($"w{w}-k{i % 16}", $"v{i}", CancellationToken.None);
		}));

		var readers = Enumerable.Range(0, 8).Select(r => Task.Run(() => {
			for (var i = 0; i < 500; i++)
				cache.TryGet($"w{i % 8}-k{i % 16}", out _);
		}));

		await Task.WhenAll(writers.Concat(readers));
	}
}
