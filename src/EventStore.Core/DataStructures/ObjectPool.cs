// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Common.Utils;
using System.Collections.Concurrent;

namespace EventStore.Core.DataStructures;

public class ObjectPoolDisposingException : Exception {
	public ObjectPoolDisposingException(string poolName)
		: this(poolName, null) {
	}

	public ObjectPoolDisposingException(string poolName, Exception innerException)
		: base(string.Format("Object pool '{0}' is disposing/disposed while Get operation is requested.", poolName),
			innerException) {
		Ensure.NotNullOrEmpty(poolName, "poolName");
	}
}

public class ObjectPoolMaxLimitReachedException : Exception {
	public ObjectPoolMaxLimitReachedException(string poolName, int maxLimit)
		: this(poolName, maxLimit, null) {
	}

	public ObjectPoolMaxLimitReachedException(string poolName, int maxLimit, Exception innerException)
		: base(string.Format("Object pool '{0}' has reached its max limit for items: {1}.", poolName, maxLimit),
			innerException) {
		Ensure.NotNullOrEmpty(poolName, "poolName");
		Ensure.Nonnegative(maxLimit, "maxLimit");
	}
}

public class ObjectPool<T> : IDisposable {
	public readonly string ObjectPoolName;

	private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
	private readonly int _maxCount;
	private readonly Func<T> _factory;
	private readonly Action<T> _dispose;
	private readonly Action<ObjectPool<T>> _onPoolDisposed;

	private int _count;
	private volatile bool _disposing;
	private int _poolDisposed;

	public ObjectPool(string objectPoolName,
		int initialCount,
		int maxCount,
		Func<T> factory,
		Action<T> dispose = null,
		Action<ObjectPool<T>> onPoolDisposed = null) {
		Ensure.NotNullOrEmpty(objectPoolName, "objectPoolName");
		Ensure.Nonnegative(initialCount, "initialCount");
		Ensure.Nonnegative(maxCount, "maxCount");
		if (initialCount > maxCount)
			throw new ArgumentOutOfRangeException("initialCount", "initialCount is greater than maxCount.");
		Ensure.NotNull(factory, "factory");

		ObjectPoolName = objectPoolName;
		_maxCount = maxCount;
		_count = initialCount;
		_factory = factory;
		_dispose = dispose ?? (x => { });
		_onPoolDisposed = onPoolDisposed ?? (x => { });

		for (int i = 0; i < initialCount; ++i) {
			_queue.Enqueue(factory());
		}
	}

	public void MarkForDisposal() {
		Dispose();
	}

	public void Dispose() {
		_disposing = true;
		Thread.MemoryBarrier();
		TryDestruct();
	}

	public T Get() {
		if (_disposing)
			throw new ObjectPoolDisposingException(ObjectPoolName);

		T item;
		if (_queue.TryDequeue(out item))
			return item;

		if (_disposing)
			throw new ObjectPoolDisposingException(ObjectPoolName);

		var newCount = Interlocked.Increment(ref _count);
		if (newCount > _maxCount)
			throw new ObjectPoolMaxLimitReachedException(ObjectPoolName, _maxCount);

		if (_disposing) {
			if (Interlocked.Decrement(ref _count) == 0)
				OnPoolDisposed(); // now we possibly should "turn light off"
			throw new ObjectPoolDisposingException(ObjectPoolName);
		}

		// if we get here, then it is safe to return newly created item to user, object pool won't be disposed 
		// until that item is returned to pool
		return _factory();
	}

	public void Return(T item) {
		_queue.Enqueue(item);
		Thread.MemoryBarrier();
		if (_disposing)
			TryDestruct();
	}

	private void TryDestruct() {
		int count = int.MaxValue;

		T item;
		while (_queue.TryDequeue(out item)) {
			_dispose(item);
			count = Interlocked.Decrement(ref _count);
		}

		if (count < 0)
			throw new Exception("Somehow we managed to decrease count of pool items below zero.");
		if (count == 0) // we are the last who should "turn the light off" 
			OnPoolDisposed();
	}

	private void OnPoolDisposed() {
		// ensure that we call _onPoolDisposed just once
		if (Interlocked.CompareExchange(ref _poolDisposed, 1, 0) == 0)
			_onPoolDisposed(this);
	}
}
