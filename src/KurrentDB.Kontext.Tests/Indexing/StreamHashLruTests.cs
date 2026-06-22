// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Indexing;

public class StreamHashLruTests {
	static StreamHashLru Make(int capacity = 3) {
		// Use reflection because the type is internal.
		var t = typeof(StreamHashLru);
		return (StreamHashLru)Activator.CreateInstance(t, capacity)!;
	}

	[Test]
	public async Task TryAdd_First_Time_Returns_True() {
		var lru = Make();
		await Assert.That(lru.TryAdd(1)).IsTrue();
	}

	[Test]
	public async Task TryAdd_Repeat_Returns_False() {
		var lru = Make();
		lru.TryAdd(1);
		await Assert.That(lru.TryAdd(1)).IsFalse();
	}

	[Test]
	public async Task Evicts_Oldest_When_Full() {
		var lru = Make(capacity: 3);
		lru.TryAdd(1);
		lru.TryAdd(2);
		lru.TryAdd(3);
		lru.TryAdd(4); // should evict 1

		await Assert.That(lru.TryAdd(2)).IsFalse(); // still present
		await Assert.That(lru.TryAdd(3)).IsFalse();
		await Assert.That(lru.TryAdd(4)).IsFalse();
		await Assert.That(lru.TryAdd(1)).IsTrue();  // 1 was evicted, re-added as new
	}

	[Test]
	public async Task Touch_Moves_To_Tail() {
		var lru = Make(capacity: 3);
		lru.TryAdd(1);
		lru.TryAdd(2);
		lru.TryAdd(3);
		lru.TryAdd(1);  // touch — moves 1 to tail; oldest is now 2

		lru.TryAdd(4); // should evict 2, not 1

		await Assert.That(lru.TryAdd(1)).IsFalse();
		await Assert.That(lru.TryAdd(3)).IsFalse();
		await Assert.That(lru.TryAdd(4)).IsFalse();
		await Assert.That(lru.TryAdd(2)).IsTrue(); // 2 was evicted
	}
}