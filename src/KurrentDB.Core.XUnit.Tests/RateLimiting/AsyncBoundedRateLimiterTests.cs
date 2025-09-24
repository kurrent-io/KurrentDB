// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using DotNext;
using KurrentDB.Core.RateLimiting;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.RateLimiting;

public class AsyncBoundedRateLimiterTests {
	[Fact]
	public static async Task AcquireRelease() {
		using var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 1, maxQueueSize: 2);
		var task = rateLimiter.AcquireAsync(prioritized: false).AsTask();

		// the lease acquired synchronously
		Assert.True(task.IsCompletedSuccessfully);
		Assert.True(await task);

		// suspended caller
		task = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task.IsCompleted);

		// resume the suspended caller
		rateLimiter.Release();
		Assert.True(await task);
	}

	[Fact]
	public static async Task DisposeRateLimiter() {
		var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 1, maxQueueSize: 2);
		var task = rateLimiter.AcquireAsync(prioritized: false).AsTask();

		// the lease acquired synchronously
		Assert.True(task.IsCompletedSuccessfully);
		Assert.True(await task);

		// suspended caller
		task = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task.IsCompleted);

		// resume the suspended caller with ObjectDisposedException
		rateLimiter.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(Func.Constant(task));

		// Ensure that call to Release() doesn't throw
		rateLimiter.Release();
	}

	[Fact]
	public static async Task PrioritizedQueueResumedFirst() {
		using var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 1, maxQueueSize: 2);
		Assert.True(await rateLimiter.AcquireAsync(prioritized: false));

		var task1 = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task1.IsCompleted);

		var task2 = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task2.IsCompleted);

		var prioritizedTask = rateLimiter.AcquireAsync(prioritized: true).AsTask();
		Assert.False(prioritizedTask.IsCompleted);

		// resume the prioritized caller
		rateLimiter.Release();
		Assert.True(await prioritizedTask);

		Assert.False(task1.IsCompleted);
		Assert.False(task2.IsCompleted);
	}

	[Fact]
	public static async Task ConcurrencyLimitReached() {
		using var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 1, maxQueueSize: 2);
		Assert.True(await rateLimiter.AcquireAsync(prioritized: false));

		var task1 = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task1.IsCompleted);

		var task2 = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task2.IsCompleted);

		Assert.False(await rateLimiter.AcquireAsync(prioritized: false));
	}

	[Fact]
	public static async Task QueueOrderPreserved() {
		using var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 1, maxQueueSize: 2);
		Assert.True(await rateLimiter.AcquireAsync(prioritized: false));

		var task1 = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task1.IsCompleted);

		var task2 = rateLimiter.AcquireAsync(prioritized: false).AsTask();
		Assert.False(task2.IsCompleted);

		rateLimiter.Release();
		Assert.True(await task1);
		Assert.False(task2.IsCompleted);

		rateLimiter.Release();
		Assert.True(await task2);
	}

	[Fact]
	public static async Task AcquireInParallel() {
		using var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 3, maxQueueSize: 0);
		await Task.WhenAll(Task.Run(AcquireAsync), Task.Run(AcquireAsync), Task.Run(AcquireAsync));
		Assert.False(await rateLimiter.AcquireAsync(prioritized: false));

		async Task AcquireAsync() {
			Assert.True(await rateLimiter.AcquireAsync(prioritized: false));
		}
	}

	[Fact]
	public static async Task StressTest() {
		using var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 3, maxQueueSize: 1);

		// start 4 competing tasks in parallel, which is larger than concurrency limit
		await Task.WhenAll(
			Task.Run(AcquireReleaseAsync),
			Task.Run(AcquireReleaseAsync),
			Task.Run(AcquireReleaseAsync),
			Task.Run(AcquireReleaseAsync));

		Assert.Equal(3, rateLimiter.RemainingLeases);

		async Task AcquireReleaseAsync() {
			for (var i = 0; i < 100; i++) {
				Assert.True(await rateLimiter.AcquireAsync(prioritized: false));
				await Task.Delay(10);
				rateLimiter.Release();
			}
		}
	}

	[Fact]
	public static async Task TimeoutWhenAcquiringLease() {
		using var rateLimiter = new AsyncBoundedRateLimiter(concurrencyLimit: 1, maxQueueSize: 1);
		Assert.True(await rateLimiter.AcquireAsync(prioritized: false, TimeSpan.Zero));

		await Assert.ThrowsAsync<TimeoutException>(async () =>
			await rateLimiter.AcquireAsync(prioritized: false, TimeSpan.Zero));

		await Assert.ThrowsAsync<TimeoutException>(async () =>
			await rateLimiter.AcquireAsync(prioritized: false, TimeSpan.FromMilliseconds(1)));
	}
}
