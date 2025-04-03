// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.DataStructures;
using NUnit.Framework;

namespace EventStore.Core.Tests.DataStructures;

[TestFixture(1)]
[TestFixture(2)]
[TestFixture(10)]
[TestFixture(13)]
[TestFixture(100)]
public class unmanaged_memory_append_only_list_should {
	private UnmanagedMemoryAppendOnlyList<int> _list;
	private readonly int _maxCapacity;

	public unmanaged_memory_append_only_list_should(int maxCapacity) {
		_maxCapacity = maxCapacity;
	}

	[SetUp]
	public void SetUp() {
		_list = new UnmanagedMemoryAppendOnlyList<int>(_maxCapacity);
	}

	[TearDown]
	public void TearDown() {
		_list?.Dispose();
	}

	[Test]
	public void correctly_add_items() {
		Assert.AreEqual(0, _list.Count);

		for (int i = 1; i <= _maxCapacity; i++) {
			_list.Add(i);
			Assert.AreEqual(i, _list.Count);
		}

		for (int i = 0; i < _maxCapacity; i++) {
			Assert.AreEqual(i + 1, _list[i]);
		}
	}

	[Test]
	public void return_correct_count_and_items_with_as_span() {
		Assert.AreEqual(0, _list.AsSpan().Length);

		for (int i = 1; i <= _maxCapacity; i++) {
			_list.Add(i);
			var span = _list.AsSpan();
			Assert.AreEqual(i,span.Length);
			for (int j = 0; j < i; j++) {
				Assert.AreEqual(j+1, span[j]);
			}
		}
	}

	[Test]
	public void correctly_clear_items() {
		Assert.AreEqual(0, _list.Count);

		//add _maxCapacity items with values 1.._maxCapacity
		for (int i = 1; i <= _maxCapacity; i++) {
			_list.Add(i);
			Assert.AreEqual(i, _list.Count);
		}

		//clear the list
		_list.Clear();
		Assert.AreEqual(0, _list.Count);

		//we should not be able to access any item at indexes 0.._maxCapacity-1
		for (int i = 0; i < _maxCapacity; i++) {
			Assert.Throws<IndexOutOfRangeException>(() => {
				var unused = _list[i];
			});
		}

		//add _maxCapacity items with values -1..-_maxCapacity
		for (int i = 1; i <= _maxCapacity; i++) {
			_list.Add(-i);
			Assert.AreEqual(i, _list.Count);
		}

		//verify that the new items have been correctly added
		for (int i = 0; i < _maxCapacity; i++) {
			Assert.AreEqual(-(i + 1), _list[i]);
		}
	}

	[Test]
	public void throw_max_capacity_reached_exception_if_list_is_full() {
		for (int i = 1; i <= _maxCapacity; i++) {
			_list.Add(0);
		}

		Assert.Throws<MaxCapacityReachedException>(() => {
			_list.Add(0);
		});
	}

	[Test]
	public void throw_index_out_of_range_exception_when_accessing_out_of_bounds_index() {
		int unused;
		Assert.Throws<IndexOutOfRangeException>(() => {
			unused = _list[-1];
		});

		Assert.Throws<IndexOutOfRangeException>(() => {
			unused = _list[-2];
		});

		for (int i = 0; i < _maxCapacity; i++) {
			Assert.Throws<IndexOutOfRangeException>(() => {
				unused = _list[i];
			});
			_list.Add(0);

			Assert.DoesNotThrow(() => {
				unused = _list[i];
			});
		}

		Assert.Throws<IndexOutOfRangeException>(() => {
			unused = _list[_maxCapacity];
		});

		Assert.Throws<IndexOutOfRangeException>(() => {
			unused = _list[_maxCapacity + 1];
		});
	}

	[Test]
	public void be_passed_by_reference() {
		Assert.AreEqual(0, _list.Count);
		Add(_list, 5);
		Assert.AreEqual(1, _list.Count);
		Assert.AreEqual(5, _list[0]);
		Clear(_list);
		Assert.AreEqual(0, _list.Count);
	}

	private static void Add(IAppendOnlyList<int> list, int value) {
		list.Add(value);
	}

	private static void Clear(IAppendOnlyList<int> list) {
		list.Clear();
	}
}

public class unmanaged_memory_append_only_list_with_non_positive_capacity_should {
	[Test]
	public void throw_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			var unused = new UnmanagedMemoryAppendOnlyList<int>(0);
		});

		Assert.Throws<ArgumentException>(() => {
			var unused = new UnmanagedMemoryAppendOnlyList<int>(-1);
		});

		Assert.Throws<ArgumentException>(() => {
			var unused = new UnmanagedMemoryAppendOnlyList<int>(-2);
		});
	}
}
