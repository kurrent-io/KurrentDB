// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.DataStructures;
using NUnit.Framework;

namespace EventStore.Core.Tests.DataStructures;

[TestFixture]
public class pairing_heap_should {
	PairingHeap<int> _heap;

	[SetUp]
	public void SetUp() {
		_heap = new PairingHeap<int>();
	}

	[TearDown]
	public void TearDown() {
		_heap = null;
	}

	[Test]
	public void throw_argumentnullexception_when_given_null_comparer() {
		Assert.Throws<ArgumentNullException>(() => new PairingHeap<int>(null as IComparer<int>));
	}

	[Test]
	public void throw_argumentnullexception_when_given_null_compare_func() {
		Assert.Throws<ArgumentNullException>(() => new PairingHeap<int>(null as Func<int, int, bool>));
	}

	[Test]
	public void throw_invalidoperationexception_when_trying_to_find_min_element_on_empty_queue() {
		Assert.Throws<InvalidOperationException>(() => _heap.FindMin());
	}

	[Test]
	public void throw_invalidoperationexception_when_trying_to_delete_min_element_on_empty_queue() {
		Assert.Throws<InvalidOperationException>(() => _heap.DeleteMin());
	}

	[Test]
	public void return_correct_min_element_and_keep_it_in_heap_on_findmin_operation() {
		_heap.Add(9);
		_heap.Add(7);
		_heap.Add(5);
		_heap.Add(3);

		Assert.That(_heap.FindMin(), Is.EqualTo(3));
		Assert.That(_heap.Count, Is.EqualTo(4));
	}

	[Test]
	public void return_correct_min_element_and_remove_it_from_heap_on_delete_min_operation() {
		_heap.Add(7);
		_heap.Add(5);
		_heap.Add(3);

		Assert.That(_heap.DeleteMin(), Is.EqualTo(3));
		Assert.That(_heap.Count, Is.EqualTo(2));
	}

	[Test]
	public void return_elements_in_sorted_order() {
		var reference = new[] {2, 5, 7, 9, 11, 27, 32};
		var returned = new List<int>();

		for (int i = reference.Length - 1; i >= 0; --i) {
			_heap.Add(reference[i]);
		}

		while (_heap.Count > 0) {
			returned.Add(_heap.DeleteMin());
		}

		Assert.That(returned, Is.EquivalentTo(reference));
	}

	[Test]
	public void keep_all_duplicates() {
		var reference = new[] {2, 5, 5, 7, 9, 9, 11, 11, 11, 27, 32};
		var returned = new List<int>();

		for (int i = reference.Length - 1; i >= 0; --i) {
			_heap.Add(reference[i]);
		}

		while (_heap.Count > 0) {
			returned.Add(_heap.DeleteMin());
		}

		Assert.That(returned, Is.EquivalentTo(reference));
	}

	[Test]
	public void handle_a_lot_of_elements_and_not_loose_any_elements() {
		var elements = new List<int>();
		var rnd = new Random(123456791);

		for (int i = 0; i < 1000; ++i) {
			var elem = rnd.Next();
			elements.Add(elem);
			_heap.Add(elem);
		}

		elements.Sort();

		var returned = new List<int>();
		while (_heap.Count > 0) {
			returned.Add(_heap.DeleteMin());
		}
	}
}
