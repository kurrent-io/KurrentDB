// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace EventStore.Core.DataStructures;


public interface IAppendOnlyList<in T> {
	void Add(T item);
	void Clear();
	int Count { get; }
}

public sealed class UnmanagedMemoryAppendOnlyList<T> : IAppendOnlyList<T>, IDisposable {
	private readonly int _maxCapacity;
	private readonly IntPtr _dataPtr = IntPtr.Zero;
	private readonly long _dataBytesAllocated;

	private int _count;
	private bool _disposed;

	public UnmanagedMemoryAppendOnlyList(int maxCapacity) {
		if (maxCapacity <= 0) {
			throw new ArgumentException($"{nameof(maxCapacity)} must be positive");
		}

		_maxCapacity = maxCapacity;
		_count = 0;
		long bytesToAllocate = (long)_maxCapacity * Marshal.SizeOf(typeof(T));
		_dataPtr = Marshal.AllocHGlobal(new IntPtr(bytesToAllocate));
		Thread.MemoryBarrier();
		_dataBytesAllocated = bytesToAllocate;

		if (_dataBytesAllocated > 0) {
			GC.AddMemoryPressure(_dataBytesAllocated);
		}
	}

	~UnmanagedMemoryAppendOnlyList() => Dispose(false);

	public void Add(T item) {
		if (_count >= _maxCapacity)
			throw new MaxCapacityReachedException();

		unsafe
		{
			new Span<T>(_dataPtr.ToPointer(), _maxCapacity) {
				[_count] = item
			};
		}
		_count++;
	}

	public void Clear() {
		_count = 0;
	}

	public int Count => _count;
	public ReadOnlySpan<T> AsSpan() {
		if (_dataPtr == IntPtr.Zero)
			return ReadOnlySpan<T>.Empty;

		unsafe {
			return new ReadOnlySpan<T>(_dataPtr.ToPointer(), _maxCapacity).Slice(0, _count);
		}
	}

	public T this[int index] {
		get {
			if (index < 0 || index >= _count) {
				throw new IndexOutOfRangeException();
			}

			unsafe
			{
				return new ReadOnlySpan<T>(_dataPtr.ToPointer(), _maxCapacity)[index];
			}
		}
	}

	public void Dispose() {
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool disposing) {
		if (_disposed) {
			return;
		}

		if (disposing) {
			//dispose any managed objects here
		}

		if (_dataPtr != IntPtr.Zero) {
			Marshal.FreeHGlobal(_dataPtr);
			Thread.MemoryBarrier();
			if (_dataBytesAllocated > 0) {
				GC.RemoveMemoryPressure(_dataBytesAllocated);
			}
		}

		_disposed = true;
	}
}

public class MaxCapacityReachedException : Exception {
	public MaxCapacityReachedException() : base("Max capacity reached") {
	}
}
