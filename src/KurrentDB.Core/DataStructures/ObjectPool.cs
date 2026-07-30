// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Threading;
using KurrentDB.Common.Utils;

namespace KurrentDB.Core.DataStructures;

public class ObjectPoolDisposingException : Exception {
	public ObjectPoolDisposingException(string poolName)
		: this(poolName, null) {
	}

	public ObjectPoolDisposingException(string poolName, Exception innerException)
		: base($"Object pool '{poolName}' is disposing/disposed while Get operation is requested.", innerException) {
		Ensure.NotNullOrEmpty(poolName, "poolName");
	}
}

public class ObjectPoolMaxLimitReachedException : Exception {
	public ObjectPoolMaxLimitReachedException(string poolName, int maxLimit)
		: this(poolName, maxLimit, null) {
	}

	public ObjectPoolMaxLimitReachedException(string poolName, int maxLimit, Exception innerException)
		: base($"Object pool '{poolName}' has reached its max limit for items: {maxLimit}.", innerException) {
		Ensure.NotNullOrEmpty(poolName, "poolName");
		Ensure.Nonnegative(maxLimit, "maxLimit");
	}
}

public class ObjectPool<T> : IDisposable {
	private readonly string _objectPoolName;
	private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
	private readonly int _maxCount;
	private readonly Func<T> _factory;
	private readonly Action<T> _dispose;
	[CanBeNull] private Action<ObjectPool<T>> _onPoolDisposed;

	private int _count;
	private volatile bool _disposing;

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

		_objectPoolName = objectPoolName;
		_maxCount = maxCount;
		_count = initialCount;
		_factory = factory;
		_dispose = dispose ?? (static _ => { });
		_onPoolDisposed = onPoolDisposed;

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
			throw new ObjectPoolDisposingException(_objectPoolName);

		if (_queue.TryDequeue(out var item))
			return item;

		if (_disposing)
			throw new ObjectPoolDisposingException(_objectPoolName);

		var newCount = Interlocked.Increment(ref _count);

		// An overflow after increment means that we can't allocate a new resource instance.
		// "Disposing" after increment means that the pool can already be disposed.
		// In both cases, we roll back the counter and run compensation logic.
		var overflow = newCount > _maxCount;
		if (overflow || _disposing) {
			var rolledBackCount = Interlocked.Decrement(ref _count);

			// After decrementing, we can be in "disposing" state even if we entered this block on overflow.
			// So we check again if we should complete the disposing.
			if (_disposing && rolledBackCount == 0) {
				OnPoolDisposed();
			}

			// Finally, when the pool is in valid state again, throw the appropriate exception.
			throw overflow
				? new ObjectPoolMaxLimitReachedException(_objectPoolName, _maxCount)
				: new ObjectPoolDisposingException(_objectPoolName);
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

		while (_queue.TryDequeue(out var item)) {
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
		Interlocked.Exchange(ref _onPoolDisposed, value: null)?.Invoke(this);
	}
}
