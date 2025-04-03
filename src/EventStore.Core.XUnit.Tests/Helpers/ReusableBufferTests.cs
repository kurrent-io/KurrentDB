// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Helpers;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Helpers;

public class ReusableBufferTests {
	private readonly ReusableBuffer _reusableBuffer;

	public ReusableBufferTests() {
		_reusableBuffer = new ReusableBuffer(defaultSize: 10);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(10)]
	[InlineData(14)]
	public void can_acquire_buffer_as_byte_array(int numBytes) {
		var buffer = _reusableBuffer.AcquireAsByteArray(numBytes);
		Assert.True(buffer.Length >= numBytes);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(10)]
	[InlineData(14)]
	public void can_acquire_buffer_as_span(int numBytes) {
		var buffer = _reusableBuffer.AcquireAsSpan(numBytes);
		Assert.Equal(numBytes, buffer.Length);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(10)]
	[InlineData(14)]
	public void can_acquire_buffer_as_memory(int numBytes) {
		var buffer = _reusableBuffer.AcquireAsMemory(numBytes);
		Assert.Equal(numBytes, buffer.Length);
	}

	[Fact]
	public void can_reacquire_buffer_if_released() {
		_reusableBuffer.AcquireAsByteArray(5);
		_reusableBuffer.Release();
		_reusableBuffer.AcquireAsByteArray(5);
	}

	[Fact]
	public void cannot_acquire_buffer_if_not_released() {
		_reusableBuffer.AcquireAsByteArray(5);
		Assert.Throws<InvalidOperationException>(() => _reusableBuffer.AcquireAsByteArray(5));
	}

	[Fact]
	public void cannot_release_buffer_if_not_acquired() {
		Assert.Throws<InvalidOperationException>(() => _reusableBuffer.Release());
	}

	[Fact]
	public void throws_when_size_zero_or_negative() {
		Assert.Throws<ArgumentOutOfRangeException>(() => _reusableBuffer.AcquireAsByteArray(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => _reusableBuffer.AcquireAsByteArray(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new ReusableBuffer(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new ReusableBuffer(-1));
	}

	[Fact]
	public void can_acquire_buffer_of_different_lengths() {
		for (int i = 1; i <= 100; i++) {
			var buffer = _reusableBuffer.AcquireAsByteArray(i);
			Assert.True(buffer.Length >= i);
			_reusableBuffer.Release();
		}

		for (int i = 100; i >= 1; i--) {
			var buffer = _reusableBuffer.AcquireAsByteArray(i);
			Assert.True(buffer.Length >= i);
			_reusableBuffer.Release();
		}
	}
}
