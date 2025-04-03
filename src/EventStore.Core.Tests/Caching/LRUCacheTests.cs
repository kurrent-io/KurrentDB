// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.DataStructures;
using NUnit.Framework;

namespace EventStore.Core.Tests.Caching;

[TestFixture]
public class LRUCacheTests {

	private static LRUCache<TKey, TValue> GenSut<TKey, TValue>(int capacity,
		LRUCache<TKey, TValue>.CalculateItemSize calculateItemSize = null,
		LRUCache<TKey, TValue>.CalculateFreedSize calculateFreedSize = null) =>
		new (string.Empty, capacity, calculateItemSize, calculateFreedSize);

	[Test]
	public void can_add_and_read_item() {
		var sut = GenSut<string, int>(3);

		Assert.AreEqual(0, sut.Size);
		Assert.AreEqual(3, sut.Capacity);

		sut.Put("key", 1337);
		Assert.AreEqual(1, sut.Size);

		sut.TryGet("key", out var read);
		Assert.AreEqual(1337, read);
	}

	[Test]
	public void can_clear_items() {
		var sut = GenSut<string, int>(10);

		sut.Put("key1", 1);
		sut.Put("key2", 2);
		sut.Put("key3", 3);

		sut.Clear();

		Assert.False(sut.TryGet("key1", out _));
		Assert.False(sut.TryGet("key2", out _));
		Assert.False(sut.TryGet("key3", out _));
	}

	[Test]
	public void least_recently_added_item_is_replaced_when_capacity_reached() {
		var sut = GenSut<string, int>(2);

		sut.Put("key1", 1);
		sut.Put("key2", 1);
		sut.Put("key3", 1);

		Assert.False(sut.TryGet("key1", out _));
		Assert.True(sut.TryGet("key2", out _));
		Assert.True(sut.TryGet("key3", out _));
	}

	[Test]
	public void least_recently_used_item_is_replaced_when_capacity_reached() {
		var sut = GenSut<string, int>(2);

		sut.Put("key1", 1);
		sut.Put("key2", 1);

		sut.TryGet("key1", out _);

		sut.Put("key3", 1);

		Assert.False(sut.TryGet("key2", out _));
		Assert.True(sut.TryGet("key1", out _));
		Assert.True(sut.TryGet("key3", out _));
	}

	public class WithCustomItemSizeCalculator {
		private readonly LRUCache<int,int> _sut;

		public WithCustomItemSizeCalculator() =>
			_sut = GenSut<int, int>(7, (k, v) => k + v);

		[Test]
		public void size_is_correct_when_items_are_added() {
			_sut.Put(1, 0);
			Assert.AreEqual(1, _sut.Size);

			_sut.Put(2, 0);
			Assert.AreEqual(3, _sut.Size);

			_sut.Put(3, 0);
			Assert.AreEqual(6, _sut.Size);

			_sut.Put(4, 0);
			// 1 & 2 should be removed
			Assert.AreEqual(7, _sut.Size);

			_sut.Put(5, 0);
			// 3 & 4 should be removed
			Assert.AreEqual(5, _sut.Size);
		}

		[Test]
		public void size_is_correct_when_items_are_updated() {
			_sut.Put(1, 0);
			_sut.Put(2, 0);
			_sut.Put(3, 0);
			_sut.Put(1, 6); // update should remove 2 & 3

			Assert.AreEqual(7, _sut.Size);
		}

		[Test]
		public void size_is_correct_when_items_are_cleared() {
			_sut.Put(1, 0);
			_sut.Put(2, 0);
			_sut.Put(3, 0);

			_sut.Clear();

			Assert.AreEqual(0, _sut.Size);
		}
	}

	public class WhenResizing {

		[Test]
		public void can_increase_capacity() {
			var sut = GenSut<int, int>(6, (k, v) => k + v);

			sut.Put(1, 0);
			sut.Put(2, 0);
			sut.Put(3, 0);

			Assert.AreEqual(6, sut.Size);

			sut.SetCapacity(10);

			Assert.AreEqual(6, sut.Size);
			Assert.AreEqual(10, sut.Capacity);
		}

		[Test]
		public void can_decrease_capacity() {
			var sut = GenSut<int, int>(6, (k, v) => k + v);

			sut.Put(1, 0);
			sut.Put(2, 0);
			sut.Put(3, 0);

			Assert.AreEqual(6, sut.Size);

			sut.SetCapacity(3);

			Assert.AreEqual(3, sut.Size);
			Assert.AreEqual(3, sut.Capacity);
		}

		[Test]
		public void can_set_capacity_to_zero() {
			var sut = GenSut<int, int>(6, (k, v) => k + v);

			sut.Put(1, 0);
			sut.Put(2, 0);
			sut.Put(3, 0);

			Assert.AreEqual(6, sut.Size);

			sut.SetCapacity(0);

			Assert.AreEqual(0, sut.Size);
			Assert.AreEqual(0, sut.Capacity);
		}

		[Test]
		public void throws_when_setting_negative_capacity() {
			var sut = GenSut<int, int>(6, (k, v) => k + v);

			Assert.Throws<ArgumentOutOfRangeException>(() =>
				sut.SetCapacity(-1));
		}
	}
}
