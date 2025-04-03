// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace EventStore.BufferManagement.Tests;

public class has_buffer_manager_fixture {
	protected BufferManager BufferManager;

	[SetUp]
	public virtual void Setup() {
		BufferManager = new BufferManager(128, 1024, 1);
	}
}

[TestFixture]
public class when_instantiating_a_bufferpool : has_buffer_manager_fixture {
	[Test]
	public void a_negative_initial_buffers_throws_an_argumentexception() {
		Assert.Throws<ArgumentException>(() => new BufferPool(-1, BufferManager));
	}

	[Test]
	public void a_null_buffer_manager_throws_an_argumentnullexception() {
		Assert.Throws<ArgumentNullException>(() => new BufferPool(12, null));
	}

	[Test]
	public void an_empty_buffer_has_a_length_of_zero() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.AreEqual(0, pool.Length);
	}

	[Test]
	public void the_requested_buffers_should_be_removed_from_the_buffer_manager() {
		int initialBuffers = BufferManager.AvailableBuffers;
		new BufferPool(10, BufferManager);
		Assert.AreEqual(initialBuffers - 10, BufferManager.AvailableBuffers);
	}
}

[TestFixture]
public class when_changing_data_in_a_bufferpool_via_indexer : has_buffer_manager_fixture {
	[Test]
	public void an_index_under_zero_throws_an_argument_exception() {
		BufferPool pool = new BufferPool(12, BufferManager);
		Assert.Throws<ArgumentException>(() => pool[-1] = 4);
	}

	[Test]
	public void data_that_has_been_set_can_read() {
		BufferPool pool = new BufferPool(1, BufferManager);
		pool[3] = 5;
		Assert.AreEqual(5, pool[3]);
	}

	[Test]
	public void length_is_updated_when_index_higher_than_count_set() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.AreEqual(0, pool.Length);
		pool[3] = 5;
		Assert.AreEqual(4, pool.Length);
	}

	[Test]
	public void a_write_will_automatically_grow_the_buffer_pool() {
		BufferPool pool = new BufferPool(1, BufferManager);
		int initialCapacity = pool.Capacity;
		pool[initialCapacity + 14] = 5;
		Assert.AreEqual(initialCapacity * 2, pool.Capacity);
	}

	[Test]
	public void a_write_past_end_will_check_out_a_buffer_from_the_buffer_pool() {
		BufferPool pool = new BufferPool(1, BufferManager);
		int initial = BufferManager.AvailableBuffers;
		pool[pool.Capacity + 14] = 5;
		Assert.AreEqual(initial - 1, BufferManager.AvailableBuffers);
	}
}

[TestFixture]
public class when_converting_to_a_byte_array : has_buffer_manager_fixture {
	[Test]
	public void the_byte_array_should_be_the_same_length_as_the_pool_with_data() {
		BufferPool pool = new BufferPool(5, BufferManager);
		for (int i = 0; i < 500; i++) {
			pool[i] = 12;
		}

		Assert.AreEqual(500, pool.ToByteArray().Length);
	}

	[Test]
	public void the_byte_array_should_have_the_same_data_as_the_pool_with_multiple_buffers() {
		BufferPool pool = new BufferPool(5, BufferManager);
		for (int i = 0; i < 5000; i++) {
			pool[i] = (byte)(i % 255);
		}

		byte[] data = pool.ToByteArray();
		for (int i = 0; i < 5000; i++) {
			Assert.AreEqual((byte)(i % 255), data[i]);
		}
	}

	[Test]
	public void the_byte_array_should_have_the_same_data_as_the_pool_with_a_single_buffer() {
		BufferPool pool = new BufferPool(5, BufferManager);
		for (int i = 0; i < 5; i++) {
			pool[i] = (byte)(i % 255);
		}

		byte[] data = pool.ToByteArray();
		for (int i = 0; i < 5; i++) {
			Assert.AreEqual((byte)(i % 255), data[i]);
		}
	}


	[Test]
	public void an_empty_pool_should_return_an_empty_array() {
		BufferPool pool = new BufferPool(1, BufferManager);
		byte[] arr = pool.ToByteArray();
		Assert.AreEqual(0, arr.Length);
	}
}

[TestFixture]
public class when_converting_to_an_effective_IEnumerable_of_arraysegments : has_buffer_manager_fixture {
	[Test]
	public void empty_returns_no_results() {
		BufferPool pool = new BufferPool(10, BufferManager);
		foreach (ArraySegment<byte> effectiveBuffer in pool.EffectiveBuffers) {
			Assert.Fail("should not have been buffers");
		}
	}

	[Test]
	public void a_single_partial_segment_can_be_returned() {
		BufferPool pool = new BufferPool(1, BufferManager);
		for (byte i = 0; i < 10; i++) {
			pool[i] = i;
		}

		List<ArraySegment<byte>> buffers = new List<ArraySegment<byte>>(pool.EffectiveBuffers);
		Assert.IsTrue(buffers.Count == 1);
		for (byte i = 0; i < 10; i++) {
			Assert.IsTrue(buffers[0].Array[buffers[0].Offset + i] == i);
		}
	}

	[Test]
	public void multiple_segments_can_be_returned() {
		BufferManager manager = new BufferManager(3, 1000, 1);
		BufferPool pool = new BufferPool(10, manager);
		for (int i = 0; i < 2500; i++) {
			pool[i] = (byte)(i % 255);
		}

		List<ArraySegment<byte>> buffers = new List<ArraySegment<byte>>(pool.EffectiveBuffers);
		Assert.IsTrue(buffers.Count == 3);
		Assert.IsTrue(buffers[0].Count == 1000);
		Assert.IsTrue(buffers[1].Count == 1000);
		Assert.IsTrue(buffers[2].Count == 500);
	}
}

[TestFixture]
public class when_reading_data_in_a_bufferpool_via_indexer : has_buffer_manager_fixture {
	[Test]
	public void if_the_index_is_past_the_length_an_argumentoutofrangeexception_is_thrown() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			var b = pool[3];
		});
	}
}


[TestFixture]
public class when_disposing_a_buffer_pool : has_buffer_manager_fixture {
	[Test]
	public void buffers_are_released_back_to_the_buffer_pool() {
		int initial = BufferManager.AvailableBuffers;
		using (new BufferPool(20, BufferManager)) {
			//sanity check (make sure they are actually gone)
			Assert.AreEqual(initial - 20, BufferManager.AvailableBuffers);
		}

		Assert.AreEqual(initial, BufferManager.AvailableBuffers);
	}
}

[TestFixture]
public class when_reading_multiple_bytes : has_buffer_manager_fixture {
	[Test]
	public void a_null_read_buffer_throws_an_argumentnullexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentNullException>(() => { pool.ReadFrom(0, null, 0, 0); });
	}

	[Test]
	public void an_offset_larger_than_the_buffer_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.ReadFrom(0, new byte[5], 8, 3); });
	}

	[Test]
	public void a_count_larger_than_the_buffer_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.ReadFrom(0, new byte[5], 3, 5); });
	}

	[Test]
	public void a_negative_count_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.ReadFrom(0, new byte[5], 3, -1); });
	}

	[Test]
	public void a_negative_offset_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.ReadFrom(0, new byte[5], -1, 1); });
	}

	[Test]
	public void count_and_offset_together_lerger_than_buffer_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.ReadFrom(0, new byte[5], 4, 2); });
	}

	[Test]
	public void reading_from_a_position_bigger_than_buffer_length_reads_nothing() {
		BufferPool pool = new BufferPool(1, BufferManager);
		pool[0] = 12;
		pool[1] = 13;
		int read = pool.ReadFrom(3, new byte[5], 0, 5);
		Assert.AreEqual(read, 0);
	}

	[Test]
	public void reading_from_a_position_plus_count_bigger_than_buffer_length_reads_the_right_amount() {
		BufferPool pool = new BufferPool(1, BufferManager);
		pool[0] = 12;
		pool[1] = 13;
		int read = pool.ReadFrom(0, new byte[5], 0, 5);
		Assert.AreEqual(read, 2);
	}

	[Test]
	public void can_read_within_a_single_buffer_with_no_offset() {
		BufferPool pool = new BufferPool(1, BufferManager);
		for (int i = 0; i < 255; i++) {
			pool[i] = (byte)i;
		}

		byte[] buffer = new byte[255];
		pool.ReadFrom(0, buffer, 0, 255);
		for (int i = 0; i < 255; i++) {
			Assert.AreEqual((byte)i, buffer[i]);
		}
	}

	[Test]
	public void can_read_from_multiple_buffers() {
		BufferPool pool = new BufferPool(1, BufferManager);
		for (int i = 0; i < 5000; i++) {
			pool[i] = (byte)(i % 255);
		}

		byte[] buffer = new byte[5000];
		pool.ReadFrom(0, buffer, 0, 5000);
		for (int i = 0; i < 5000; i++) {
			Assert.AreEqual((byte)(i % 255), buffer[i]);
		}
	}

	[Test]
	public void can_read_using_an_offset() {
		BufferPool pool = new BufferPool(1, BufferManager);
		for (int i = 5; i < 260; i++) {
			pool[i] = (byte)(i - 5);
		}

		byte[] buffer = new byte[255];
		pool.ReadFrom(5, buffer, 0, 255);
		for (int i = 0; i < 255; i++) {
			Assert.AreEqual((byte)i, buffer[i]);
		}
	}
}

[TestFixture]
public class when_writing_multiple_bytes : has_buffer_manager_fixture {
	[Test]
	public void a_null_byte_array_throws_an_argumentnullexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentNullException>(() => { pool.Append(null); });
	}

	[Test]
	public void an_offset_larger_than_the_buffer_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.Write(0, new byte[5], 8, 3); });
	}

	[Test]
	public void a_count_larger_than_the_buffer_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.Write(0, new byte[5], 3, 5); });
	}

	[Test]
	public void a_negative_count_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.Write(0, new byte[5], 3, -1); });
	}

	[Test]
	public void a_negative_offset_throws_an_argumentoutofrangeexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentOutOfRangeException>(() => { pool.Write(0, new byte[5], -1, 1); });
	}

	[Test]
	public void length_is_updated_to_include_bytes_written() {
		BufferPool pool = new BufferPool(1, BufferManager);
		byte[] data = {1, 2, 3, 4, 5};
		pool.Append(data);
		Assert.IsTrue(pool.Length == 5);
	}

	[Test]
	public void data_is_written_to_the_internal_buffer() {
		BufferPool pool = new BufferPool(1, BufferManager);
		byte[] data = {1, 2, 3, 4, 5};
		pool.Append(data);
		for (byte i = 0; i < 5; i++) {
			Assert.AreEqual(i + 1, pool[i]);
		}
	}

	[Test]
	public void pool_can_expand_capacity() {
		BufferPool pool = new BufferPool(1, BufferManager);
		int initialCapacity = pool.Capacity;
		byte[] data = new byte[initialCapacity + 25];
		pool.Append(data);
		Assert.AreEqual(initialCapacity * 2, pool.Capacity);
	}

	[Test]
	public void can_write_given_a_self_offset() {
		BufferPool pool = new BufferPool(1, BufferManager);
		byte[] data = {1, 2, 3, 4, 5};
		pool.Write(4, data, 0, 5); //start at position 4
		for (byte i = 4; i < 9; i++) {
			Assert.AreEqual(i - 3, pool[i]);
		}
	}

	[Test]
	public void can_write_given_a_source_offset() {
		BufferPool pool = new BufferPool(1, BufferManager);
		byte[] data = {1, 2, 3, 4, 5};
		pool.Write(0, data, 3, 2);
		Assert.AreEqual(pool[0], 4);
		Assert.AreEqual(pool[1], 5);
	}
}

[TestFixture]
public class when_setting_the_length_of_the_pool : has_buffer_manager_fixture {
	[Test]
	public void a_negative_length_throws_an_argumentexception() {
		BufferPool pool = new BufferPool(1, BufferManager);
		Assert.Throws<ArgumentException>(() => { pool.SetLength(-1, false); });
	}

	[Test]
	public void a_larger_length_makes_capacity_larger() {
		BufferManager manager = new BufferManager(10, 1000, 1);
		BufferPool pool = new BufferPool(1, manager);
		pool.SetLength(5000);
		Assert.AreNotEqual(5000, pool.Capacity);
	}

	[Test]
	public void length_is_set_when_setting_length() {
		BufferPool pool = new BufferPool(1, BufferManager);
		pool.SetLength(5000, false);
		Assert.AreEqual(5000, pool.Length);
	}

	[Test]
	public void a_smaller_length_lowers_capacity() {
		BufferManager manager = new BufferManager(10, 1000, 1);
		BufferPool pool = new BufferPool(5, manager);
		pool.SetLength(1);
		Assert.AreEqual(9, manager.AvailableBuffers);
	}

	[Test]
	public void a_smaller_length_checks_buffers_back_in_when_allowed() {
		BufferManager manager = new BufferManager(10, 1000, 1);
		BufferPool pool = new BufferPool(5, manager);
		pool.SetLength(1, true);
		Assert.AreEqual(9, manager.AvailableBuffers);
	}

	[Test]
	public void a_smaller_length_checks_buffers_back_in_when_not_allowed() {
		BufferManager manager = new BufferManager(10, 1000, 1);
		BufferPool pool = new BufferPool(5, manager);
		pool.SetLength(1, false);
		Assert.AreEqual(5, manager.AvailableBuffers);
	}
}

[TestFixture]
public class when_a_buffer_pool_has_been_disposed : has_buffer_manager_fixture {
	private BufferPool m_DisposedPool;

	public override void Setup() {
		base.Setup();
		m_DisposedPool = new BufferPool(10, BufferManager);
		m_DisposedPool.Dispose();
	}

	[Test]
	public void reading_indexer_throws_objectdisposedexception() {
		Assert.Throws<ObjectDisposedException>(() => {
			byte b = m_DisposedPool[0];
		});
	}

	[Test]
	public void writing_indexer_throws_objectdisposedexception() {
		Assert.Throws<ObjectDisposedException>(() => { m_DisposedPool[0] = 5; });
	}

	[Test]
	public void writing_multiple_bytes_throws_objectdisposedexception() {
		Assert.Throws<ObjectDisposedException>(() => { m_DisposedPool.Append(new byte[] {1, 2, 3, 4}); });
	}


	[Test]
	public void effective_enumerator_throws_objectdisposedexception() {
		Assert.Throws<ObjectDisposedException>(() => {
			foreach (ArraySegment<byte> segment in m_DisposedPool.EffectiveBuffers) {
			}
		});
	}

	[Test]
	public void setting_length_throws_objectdisposedexception() {
		Assert.Throws<ObjectDisposedException>(() => { m_DisposedPool.SetLength(200); });
	}

	[Test]
	public void converting_to_a_byte_array_throws_objectdisposedexception() {
		Assert.Throws<ObjectDisposedException>(() => { m_DisposedPool.ToByteArray(); });
	}
}
