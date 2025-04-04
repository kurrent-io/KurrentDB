// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace EventStore.BufferManagement.Tests;

[TestFixture]
public class when_creating_a_buffer_manager {
	[Test]
	public void a_zero_chunk_size_causes_an_argumentexception() {
		Assert.Throws<ArgumentException>(() => new BufferManager(1024, 0, 1024));
	}

	[Test]
	public void a_negative_chunk_size_causes_an_argumentexception() {
		Assert.Throws<ArgumentException>(() => new BufferManager(200, -1, 200));
	}

	[Test]
	public void a_negative_chunks_per_segment_causes_an_argumentexception() {
		Assert.Throws<ArgumentException>(() => new BufferManager(-1, 1024, 8));
	}

	[Test]
	public void a_zero_chunks_per_segment_causes_an_argumentexception() {
		Assert.Throws<ArgumentException>(() => new BufferManager(0, 1024, 8));
	}

	[Test]
	public void a_negative_number_of_segments_causes_an_argumentexception() {
		Assert.Throws<ArgumentException>(() => new BufferManager(1024, 1024, -1));
	}

	[Test]
	public void can_create_a_manager_with_zero_inital_segments() {
		Assert.DoesNotThrow(() => new BufferManager(1024, 1024, 0));
	}
}

[TestFixture]
public class when_checking_out_a_buffer {
	[Test]
	public void should_return_a_valid_buffer_when_available() {
		BufferManager manager = new BufferManager(1, 1000, 1);
		ArraySegment<byte> buffer = manager.CheckOut();
		Assert.AreEqual(1000, buffer.Count);
	}

	[Test]
	public void should_decrement_available_buffers() {
		BufferManager manager = new BufferManager(1, 1000, 1);
		manager.CheckOut();
		Assert.AreEqual(0, manager.AvailableBuffers);
	}

	[Test]
	public void should_create_a_segment_if_none_are_availabke() {
		BufferManager manager = new BufferManager(10, 1000, 0);
		manager.CheckOut();
		Assert.AreEqual(9, manager.AvailableBuffers);
	}

	[Test]
	public void should_throw_an_unabletocreatememoryexception_if_acquiring_memory_is_disabled_and_out_of_memory() {
		BufferManager manager = new BufferManager(1, 1000, 1, false);
		manager.CheckOut();
		//should be none left, boom
		Assert.Throws<UnableToCreateMemoryException>(() => manager.CheckOut());
	}

	[Test]
	public void should_release_acquired_buffers_if_size_requirement_cant_be_satisfied() {
		BufferManager manager = new BufferManager(1, 1000, 1, false);
		Assert.Throws(Is.InstanceOf(typeof(Exception)), () => manager.CheckOut(2));
		Assert.AreEqual(1, manager.AvailableBuffers);
	}
}

[TestFixture]
public class when_checking_in_a_buffer {
	[Test]
	public void should_accept_a_checked_out_buffer() {
		BufferManager manager = new BufferManager(10, 1000, 0);
		manager.CheckIn(manager.CheckOut());
	}

	[Test]
	public void should_increment_available_buffers() {
		BufferManager manager = new BufferManager(10, 1000, 0);
		manager.CheckIn(manager.CheckOut());
		Assert.AreEqual(10, manager.AvailableBuffers);
	}

	[Test]
	public void should_throw_argumentnullexception_if_null_buffer() {
		BufferManager manager = new BufferManager(10, 1000, 0);
		Assert.Throws<ArgumentNullException>(() => { manager.CheckIn((IEnumerable<ArraySegment<byte>>)null); });
	}

	[Test]
	public void should_throw_argumentexception_if_buffer_wrong_size() {
		BufferManager manager = new BufferManager(10, 1000, 0);
		byte[] data = new byte[10000];
		Assert.Throws<ArgumentException>(() => { manager.CheckIn(new ArraySegment<byte>(data)); });
	}
}
