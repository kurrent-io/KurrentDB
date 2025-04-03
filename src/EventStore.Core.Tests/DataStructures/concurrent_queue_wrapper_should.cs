// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Core.Tests.DataStructures;

[TestFixture]
public class concurrent_queue_wrapper_should {
	[Test]
	public void return_correct_counts_and_values_1() {
		var queue = new ConcurrentQueueWrapper<int>();
		Assert.AreEqual(0, queue.Count);
		queue.Enqueue(1234);
		Assert.AreEqual(1, queue.Count);
		queue.Enqueue(12345);
		Assert.AreEqual(2, queue.Count);

		int val;
		Assert.AreEqual(true, queue.TryDequeue(out val));
		Assert.AreEqual(1234, val);
		Assert.AreEqual(1, queue.Count);

		Assert.AreEqual(true, queue.TryDequeue(out val));
		Assert.AreEqual(12345, val);
		Assert.AreEqual(0, queue.Count);

		Assert.AreEqual(false, queue.TryDequeue(out val));
		Assert.AreEqual(false, queue.TryDequeue(out val));
	}

	[Test]
	public void return_correct_counts_and_values_2() {
		var queue = new ConcurrentQueueWrapper<int>();
		for (int i = 1; i <= 1000; i++) {
			queue.Enqueue(i);
			Assert.AreEqual(i, queue.Count);
		}

		int x;
		for (int i = 1; i <= 1000; i++) {
			Assert.AreEqual(true, queue.TryDequeue(out x));
			Assert.AreEqual(i, x);
			Assert.AreEqual(1000-i, queue.Count);
		}
	}
}

[TestFixture]
public class concurrent_queue_wrapper_with_parallel_dequeues_should {
	private ConcurrentQueueWrapper<int> _concurrentQueue;
	private const int TOTAL_ENQUEUED_ITEMS = 1000000;
	private const int NUM_THREADS = 4;
	private volatile bool _seenNegativeCount;
	private volatile int _totalDequeued;

	public concurrent_queue_wrapper_with_parallel_dequeues_should() {
		_concurrentQueue = new ConcurrentQueueWrapper<int>();
		_seenNegativeCount = false;
		_totalDequeued = 0;
	}

	private void DequeueThread() {
		int x;
		while (_totalDequeued < TOTAL_ENQUEUED_ITEMS) {
			var dequeued = _concurrentQueue.TryDequeue(out x);
			if (_concurrentQueue.Count < 0) _seenNegativeCount = true;
			if (dequeued) Interlocked.Increment(ref _totalDequeued);
		}
	}

	[Test]
	public void always_return_non_negative_count() {
		var threads = new Thread[NUM_THREADS];
		for (int i = 0; i < NUM_THREADS; i++) {
			threads[i] = new Thread(DequeueThread);
			threads[i].Start();
		}

		int x;
		for (int i = 0; i < TOTAL_ENQUEUED_ITEMS; i++) {
			_concurrentQueue.Enqueue(1);
			var dequeued = _concurrentQueue.TryDequeue(out x);
			if (dequeued) Interlocked.Increment(ref _totalDequeued);
		}

		for(int i=0;i<NUM_THREADS;i++)
			threads[i].Join(TimeSpan.FromSeconds(5));

		Assert.AreEqual(false, _seenNegativeCount);
	}
}
