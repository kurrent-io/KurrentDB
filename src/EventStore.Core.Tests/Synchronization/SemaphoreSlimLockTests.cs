// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Synchronization;
using NUnit.Framework;

namespace EventStore.Core.Tests.Synchronization;

[TestFixture]
public class SemaphoreSlimLockTests {
	private SemaphoreSlimLock _semaphoreSlimLock;

	[SetUp]
	public void SetUp() => _semaphoreSlimLock = new();

	[TearDown]
	public void TearDown() => _semaphoreSlimLock?.Dispose();

	[Test]
	public void can_lock() {
		Assert.True(_semaphoreSlimLock.TryAcquire(out _));
	}

	[Test]
	public void can_unlock() {
		Assert.True(_semaphoreSlimLock.TryAcquire(out var id));
		Assert.True(_semaphoreSlimLock.TryRelease(id));
	}

	[Test]
	public void cannot_unlock_with_wrong_id() {
		Assert.True(_semaphoreSlimLock.TryAcquire(out _));
		Assert.False(_semaphoreSlimLock.TryRelease(Guid.NewGuid()));
	}

	[Test]
	public void cannot_unlock_twice_with_same_id() {
		Assert.True(_semaphoreSlimLock.TryAcquire(out var id));
		Assert.True(_semaphoreSlimLock.TryRelease(id));
		Assert.False(_semaphoreSlimLock.TryRelease(id));
	}

	[Test]
	public void cannot_lock_when_locked() {
		Assert.True(_semaphoreSlimLock.TryAcquire(out _));
		Assert.False(_semaphoreSlimLock.TryAcquire(out var id));
		Assert.AreEqual(Guid.Empty, id);
	}

	[Test]
	public void cannot_unlock_when_unlocked() {
		Assert.False(_semaphoreSlimLock.TryRelease(Guid.NewGuid()));
	}

	[Test]
	public void cannot_unlock_with_empty_id() {
		Assert.False(_semaphoreSlimLock.TryRelease(Guid.Empty));
	}
}
