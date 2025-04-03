// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

// net6 can do aligned allocations directly
// but we need to overallocate and do our own alignment.
public sealed unsafe class AlignedMemory : IDisposable {
	private readonly IntPtr _intPtr;
	private readonly long _size;
	private readonly long _bytesAllocated;
	private bool _disposed;

	/// contents of memory are not initialized
	public AlignedMemory(long size, int alignTo) : this(new IntPtr(size), alignTo) {
	}

	// todo probably better to use a safe handle
	~AlignedMemory() => Dispose();

	/// contents of memory are not initialized
	public AlignedMemory(IntPtr size, int alignTo) {
		_size = (long)size;
		var bytesToAllocate = size + alignTo;
		_intPtr = Marshal.AllocHGlobal(bytesToAllocate);
		_bytesAllocated = (long)bytesToAllocate;
		GC.AddMemoryPressure(_bytesAllocated);
		Pointer = (byte*)((long)_intPtr).RoundUpToMultipleOf(alignTo);
	}

	public byte* Pointer { get; }

	public Span<byte> AsSpan() {
		if (_size > int.MaxValue)
			throw new InvalidOperationException("Size is too big to fit in one span");
		return new(Pointer, (int)_size);
	}

	public void Dispose() {
		if (_disposed)
			return;

		_disposed = true;
		GC.SuppressFinalize(this);

		if (_intPtr == IntPtr.Zero) {
			// didn't manage to allocate or add pressure (OOM), so do not free.
			return;
		}

		Marshal.FreeHGlobal(_intPtr);
		GC.RemoveMemoryPressure(_bytesAllocated);
	}
}
