// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Caching;
using NUnit.Framework;

namespace EventStore.Core.Tests.Caching;

// these tests may break in the future if there are changes in the CLR.
// the constants can then be adjusted accordingly.
[TestFixture]
public class MemSizerTests {

	private class TestObject1 {
		public readonly long TestField1;

		public TestObject1() {
			TestField1 = 0;
		}
	}

	private class TestObject2 {
		public readonly long TestField1;
		public readonly long TestField2;

		public TestObject2() {
			TestField1 = 0;
			TestField2 = 0;
		}
	}

	private struct TestStruct {
		public readonly long TestField1;
		public readonly int TestField2;
		public readonly bool TestField3;
		public readonly int TestField4;

		public TestStruct(long testField1, int testField2, bool testField3, int testField4) {
			TestField1 = testField1;
			TestField2 = testField2;
			TestField3 = testField3;
			TestField4 = testField4;
		}
	}

	[Test]
	public void object_header_size_is_correct() {
		// .NET objects have a minimum size of 24 bytes, but the object header size is 16 bytes long.
		// to be able to test the correctness of the object header size constant, we need to have at
		// least one field in the object
		Assert.AreEqual(
			MemSizer.ObjectHeaderSize + sizeof(long),
			MemUsage.Calculate(() => new TestObject1()));

		Assert.AreEqual(
			MemSizer.ObjectHeaderSize + 2 * sizeof(long),
			MemUsage.Calculate(() => new TestObject2()));
	}

	[Test]
	public void array_size_is_correct() {
		Assert.AreEqual(
			MemSizer.ArraySize,
			MemUsage.Calculate(() => new object[0], out _));

		Assert.AreEqual(
			MemSizer.ArraySize,
			MemUsage.Calculate(() => new string[0], out _));
	}

	[Test]
	public void string_size_is_correct() {
		var mem = MemUsage.Calculate(() => new string('x', 1234), out var s);
		Assert.AreEqual(mem, MemSizer.SizeOf(s));
	}

	[Test]
	public void empty_string_size_is_correct() {
		var mem = MemUsage.Calculate(() => new string('x', 0), out var s);
		Assert.AreEqual(mem, MemSizer.SizeOf(s));
	}

	[Test]
	public void string_array_size_is_correct() {
		var mem = MemUsage.Calculate(() => {
			var s1 = new string('x', 123);
			var s2 = new string('y', 321);
			return new[] { s1, s2 };
		}, out var arr);

		Assert.AreEqual(mem, MemSizer.SizeOf(arr));
	}

	[TestCase("key", "value")]
	[TestCase("key", 12)]
	[TestCase("key", 12L)]
	[TestCase(12, 12L)]
	[TestCase(12L, 12)]
	[TestCase("key", true)]
	[TestCase(12, true)]
	public void dictionary_entry_size_is_correct_with_primitives<TKey, TValue>(TKey key, TValue value) {
		var mem3 = MemUsage.Calculate(() => new Dictionary<TKey, TValue>(3));
		var mem7 = MemUsage.Calculate(() => new Dictionary<TKey, TValue>(7));

		Assert.AreEqual(mem7 - mem3, MemSizer.SizeOfDictionaryEntry<TKey, TValue>() * 4);
	}


	[Test]
	public void dictionary_entry_size_is_correct_with_object() {
		var mem3 = MemUsage.Calculate(() => new Dictionary<string, TestObject2>(3));
		var mem7 = MemUsage.Calculate(() => new Dictionary<string, TestObject2>(7));

		Assert.AreEqual(mem7 - mem3, MemSizer.SizeOfDictionaryEntry<string, TestObject2>() * 4);
	}

	[Test]
	public void dictionary_entry_size_is_correct_with_struct() {
		var mem3 = MemUsage.Calculate(() => new Dictionary<string, TestStruct>(3));
		var mem7 = MemUsage.Calculate(() => new Dictionary<string, TestStruct>(7));

		Assert.AreEqual(mem7 - mem3, MemSizer.SizeOfDictionaryEntry<string, TestStruct>() * 4);
	}

	[TestCase("string")]
	[TestCase(12)]
	[TestCase(12L)]
	[TestCase(true)]
	public void linked_list_node_size_is_correct_with_primitives<T>(T item) {
		var mem = MemUsage.Calculate(() => new LinkedListNode<T>(item));

		Assert.AreEqual(mem, MemSizer.SizeOfLinkedListNode<T>());
	}

	[Test]
	public void linked_list_node_size_is_correct_with_object() {
		var x = new TestObject1();
		var mem = MemUsage.Calculate(() => new LinkedListNode<TestObject1>(x));

		Assert.AreEqual(mem, MemSizer.SizeOfLinkedListNode<TestObject1>());
	}

	[Test]
	public void linked_list_node_size_is_correct_with_struct() {
		var x = new TestStruct();
		var mem = MemUsage.Calculate(() => new LinkedListNode<TestStruct>(x));

		Assert.AreEqual(mem, MemSizer.SizeOfLinkedListNode<TestStruct>());
	}

	[Test]
	public void linked_list_entry_size_is_correct() {
		var linkedList = new LinkedList<string>();
		var node = new LinkedListNode<string>("test");

		var mem = MemUsage.Calculate(() => linkedList.AddLast(node));

		Assert.AreEqual(mem, MemSizer.LinkedListEntrySize);
	}
}
