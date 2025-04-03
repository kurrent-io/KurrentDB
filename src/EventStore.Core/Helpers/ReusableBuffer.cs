// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EventStore.Core.Helpers;

// Thread-safe reusable buffer: similar concept as an ArrayPool<byte> but has only one buffer and one user at a time.
// If used correctly, there should not be any contention since there can be only one user of the buffer at a time.
// However, some synchronization is done since the AcquireAs*() and Release() methods are allowed to be called from
// different threads.
public class ReusableBuffer {
	private byte[] _buffer;
	private int _state;

	private enum State {
		Free = 0,
		LockedToAcquire = 1,
		Acquired = 2,
	}

	public ReusableBuffer(int defaultSize) {
		if (defaultSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(defaultSize), "default size must be positive");

		_buffer = new byte[ClosestPowerOf2(defaultSize)];
		_state = (int)State.Free;
	}

	private static int ClosestPowerOf2(int x) {
		int y = 0;
		while (x > 0) {
			x >>= 1;
			y++;
		}

		if (y >= 31)
			throw new ArgumentOutOfRangeException();

		return 1 << y;
	}

	// Note: The acquired buffer size can be larger than the requested size
	// It is better to use AcquireAsSpan() or AcquireAsMemory() where possible.
	public byte[] AcquireAsByteArray(int size) {
		if (size <= 0)
			throw new ArgumentOutOfRangeException(nameof(size), "size must be positive");

		TrySwitchState(State.Free, State.LockedToAcquire);

		if (_buffer.Length < size)
			_buffer = new byte[ClosestPowerOf2(size)];

		TrySwitchState(State.LockedToAcquire, State.Acquired);
		return _buffer;
	}

	public Span<byte> AcquireAsSpan(int size) => AcquireAsByteArray(size).AsSpan(0, size);

	public Memory<byte> AcquireAsMemory(int size) => AcquireAsByteArray(size).AsMemory(0, size);

	public void Release() {
		TrySwitchState(State.Acquired, State.Free);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void TrySwitchState(State from, State to) {
		var was = (State)Interlocked.CompareExchange(ref _state, (int)to, (int)from);
		if (was != from)
			throw new InvalidOperationException($"Failed to transition buffer from state: {from} to {to}. Was {was}.");
	}
}
