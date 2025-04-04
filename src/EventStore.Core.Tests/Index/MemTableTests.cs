// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index;

[TestFixture]
public class HashListMemTableTests : MemTableTestsFixture {
	public HashListMemTableTests()
		: base(() => new HashListMemTable(PTableVersions.IndexV2, maxSize: 20)) {
	}
}

[TestFixture]
public abstract class MemTableTestsFixture {
	private readonly Func<IMemTable> _memTableFactory;

	protected IMemTable MemTable;

	protected MemTableTestsFixture(Func<IMemTable> memTableFactory) {
		_memTableFactory = memTableFactory;
		Ensure.NotNull(memTableFactory, "memTableFactory");
	}

	[SetUp]
	public void SetUp() {
		MemTable = _memTableFactory();
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_range_query_when_provided_with_negative_start_version() {
		Assert.Throws<ArgumentOutOfRangeException>(() => MemTable.GetRange(0x0000, -1, int.MaxValue).ToArray());
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_range_query_when_provided_with_negative_end_version() {
		Assert.Throws<ArgumentOutOfRangeException>(() => MemTable.GetRange(0x0000, 0, -1).ToArray());
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_get_one_entry_query_when_provided_with_negative_version() {
		long pos;
		Assert.Throws<ArgumentOutOfRangeException>(() => MemTable.TryGetOneValue(0x0000, -1, out pos));
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_adding_entry_with_negative_version() {
		Assert.Throws<ArgumentOutOfRangeException>(() => MemTable.Add(0x0000, -1, 0));
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_adding_entry_with_negative_position() {
		Assert.Throws<ArgumentOutOfRangeException>(() => MemTable.Add(0x0000, 0, -1));
	}

	[Test]
	public void empty_memtable_has_count_of_zero() {
		Assert.AreEqual(0, MemTable.Count);
	}

	[Test]
	public void adding_an_item_increments_count() {
		MemTable.Add(0x11, 0x01, 0xffff);
		Assert.AreEqual(1, MemTable.Count);
	}

	[Test]
	public void non_existent_item_is_not_found() {
		MemTable.Add(0x11, 0x01, 0xffff);
		long position;
		Assert.IsFalse(MemTable.TryGetOneValue(0x11, 0x02, out position));
	}


	[Test]
	public void existing_item_is_found() {
		MemTable.Add(0x11, 0x01, 0xffff);
		long position;
		Assert.IsTrue(MemTable.TryGetOneValue(0x11, 0x01, out position));
	}

	[Test]
	public void items_come_out_sorted() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		var list = new List<IndexEntry>(MemTable.IterateAllInOrder());
		Assert.AreEqual(3, list.Count);
		Assert.AreEqual(0x12, list[0].Stream);
		Assert.AreEqual(0x01, list[0].Version);
		Assert.AreEqual(0xfff3, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x02, list[1].Version);
		Assert.AreEqual(0xfff2, list[1].Position);
		Assert.AreEqual(0x11, list[2].Stream);
		Assert.AreEqual(0x01, list[2].Version);
		Assert.AreEqual(0xfff1, list[2].Position);
	}

	[Test]
	public void items_come_out_sorted_with_duplicates_in_descending_order() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x12, 0x01, 0xfff4);
		var list = new List<IndexEntry>(MemTable.IterateAllInOrder());
		Assert.AreEqual(3, list.Count);
		Assert.AreEqual(0x12, list[0].Stream);
		Assert.AreEqual(0x01, list[0].Version);
		Assert.AreEqual(0xfff4, list[0].Position);
		Assert.AreEqual(0x12, list[1].Stream);
		Assert.AreEqual(0x01, list[1].Version);
		Assert.AreEqual(0xfff3, list[1].Position);
		Assert.AreEqual(0x11, list[2].Stream);
		Assert.AreEqual(0x01, list[2].Version);
		Assert.AreEqual(0xfff1, list[2].Position);
	}

	[Test]
	public void can_do_range_query_of_existing_items() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x01, 0x02));
		Assert.AreEqual(2, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x02, list[0].Version);
		Assert.AreEqual(0xfff2, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x01, list[1].Version);
		Assert.AreEqual(0xfff1, list[1].Position);
	}

	[Test]
	public void can_do_range_query_of_existing_items_with_duplicates_on_edges() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff5);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		MemTable.Add(0x11, 0x02, 0xfff7);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x01, 0x02));
		Assert.AreEqual(4, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x02, list[0].Version);
		Assert.AreEqual(0xfff7, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x02, list[1].Version);
		Assert.AreEqual(0xfff2, list[1].Position);
		Assert.AreEqual(0x11, list[2].Stream);
		Assert.AreEqual(0x01, list[2].Version);
		Assert.AreEqual(0xfff5, list[2].Position);
		Assert.AreEqual(0x11, list[3].Stream);
		Assert.AreEqual(0x01, list[3].Version);
		Assert.AreEqual(0xfff1, list[3].Position);
	}

	[Test]
	public void range_query_of_non_existing_stream_returns_nothing() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		var list = new List<IndexEntry>(MemTable.GetRange(0x14, 0x01, 0x02));
		Assert.AreEqual(0, list.Count);
	}

	[Test]
	public void range_query_of_non_existing_version_returns_nothing() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		var list = new List<IndexEntry>(MemTable.GetRange(0x01, 0x03, 0x05));
		Assert.AreEqual(0, list.Count);
	}


	[Test]
	public void range_query_with_hole_returns_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		MemTable.Add(0x11, 0x04, 0xfff4);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x01, 0x04));
		Assert.AreEqual(3, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x04, list[0].Version);
		Assert.AreEqual(0xfff4, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x02, list[1].Version);
		Assert.AreEqual(0xfff2, list[1].Position);
		Assert.AreEqual(0x11, list[2].Stream);
		Assert.AreEqual(0x01, list[2].Version);
		Assert.AreEqual(0xfff1, list[2].Position);
	}

	[Test]
	public void query_with_start_in_range_but_not_end_results_returns_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x01, 0x04));
		Assert.AreEqual(2, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x02, list[0].Version);
		Assert.AreEqual(0xfff2, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x01, list[1].Version);
		Assert.AreEqual(0xfff1, list[1].Position);
	}

	[Test]
	public void query_with_end_in_range_but_not_start_results_returns_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x00, 0x02));
		Assert.AreEqual(2, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x02, list[0].Version);
		Assert.AreEqual(0xfff2, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x01, list[1].Version);
		Assert.AreEqual(0xfff1, list[1].Position);
	}

	[Test]
	public void query_with_end_and_start_exclusive_results_returns_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x12, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff2);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x00, 0x04));
		Assert.AreEqual(2, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x02, list[0].Version);
		Assert.AreEqual(0xfff2, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x01, list[1].Version);
		Assert.AreEqual(0xfff1, list[1].Position);
	}

	[Test]
	public void query_with_end_inside_the_hole_in_list_returns_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x00, 0x04));
		Assert.AreEqual(2, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x03, list[0].Version);
		Assert.AreEqual(0xfff3, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x01, list[1].Version);
		Assert.AreEqual(0xfff1, list[1].Position);
	}

	[Test]
	public void query_with_end_inside_the_hole_in_list_returns_items_included_with_duplicates() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x03, 0xfff2);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x00, 0x04));
		Assert.AreEqual(3, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x03, list[0].Version);
		Assert.AreEqual(0xfff3, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x03, list[1].Version);
		Assert.AreEqual(0xfff2, list[1].Position);
		Assert.AreEqual(0x11, list[2].Stream);
		Assert.AreEqual(0x01, list[2].Version);
		Assert.AreEqual(0xfff1, list[2].Position);
	}

	[Test]
	public void query_with_start_inside_the_hole_in_list_returns_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x02, 0x06));
		Assert.AreEqual(2, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x05, list[0].Version);
		Assert.AreEqual(0xfff5, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x03, list[1].Version);
		Assert.AreEqual(0xfff3, list[1].Position);
	}

	[Test]
	public void query_with_start_inside_the_hole_in_list_returns_duplicated_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x03, 0xfff2);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x02, 0x06));
		Assert.AreEqual(3, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x05, list[0].Version);
		Assert.AreEqual(0xfff5, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x03, list[1].Version);
		Assert.AreEqual(0xfff3, list[1].Position);
		Assert.AreEqual(0x11, list[2].Stream);
		Assert.AreEqual(0x03, list[2].Version);
		Assert.AreEqual(0xfff2, list[2].Position);
	}

	[Test]
	public void query_with_start_and_end_inside_the_hole_in_list_returns_items_included() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x02, 0x04));
		Assert.AreEqual(1, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x03, list[0].Version);
		Assert.AreEqual(0xfff3, list[0].Position);
	}

	[Test]
	public void query_with_start_and_end_inside_the_hole_in_list_returns_items_included_with_duplicates() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff2);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x03, 0xfff4);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x02, 0x04));
		Assert.AreEqual(2, list.Count);
		Assert.AreEqual(0x11, list[0].Stream);
		Assert.AreEqual(0x03, list[0].Version);
		Assert.AreEqual(0xfff4, list[0].Position);
		Assert.AreEqual(0x11, list[1].Stream);
		Assert.AreEqual(0x03, list[1].Version);
		Assert.AreEqual(0xfff3, list[1].Position);
	}

	[Test]
	public void query_with_start_and_end_less_than_all_items_returns_nothing() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x00, 0x00));
		Assert.AreEqual(0, list.Count);
	}

	[Test]
	public void query_with_start_and_end_greater_than_all_items_returns_nothing() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x03, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var list = new List<IndexEntry>(MemTable.GetRange(0x11, 0x06, 0x06));
		Assert.AreEqual(0, list.Count);
	}

	[Test]
	public void try_get_one_value_returns_value_with_highest_position() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		long position;
		Assert.IsTrue(MemTable.TryGetOneValue(0x11, 0x01, out position));
		Assert.AreEqual(0xfff3, position);
	}

	[Test]
	public void get_range_of_same_version_returns_both_values_in_descending_order_when_duplicated() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		var entries = MemTable.GetRange(0x11, 0x01, 0x01).ToArray();
		Assert.AreEqual(2, entries.Length);
		Assert.AreEqual(0x11, entries[0].Stream);
		Assert.AreEqual(0x01, entries[0].Version);
		Assert.AreEqual(0xfff3, entries[0].Position);
		Assert.AreEqual(0x11, entries[1].Stream);
		Assert.AreEqual(0x01, entries[1].Version);
		Assert.AreEqual(0xfff1, entries[1].Position);
	}

	[Test]
	public void try_get_one_value_returns_the_value_with_largest_position_when_triduplicated() {
		MemTable.Add(0x01, 0x05, 0xfff9);
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		MemTable.Add(0x11, 0x01, 0xfff2);

		long position;
		Assert.IsTrue(MemTable.TryGetOneValue(0x11, 0x01, out position));
		Assert.AreEqual(0xfff3, position);
	}

	[Test]
	public void get_range_of_same_version_returns_both_values_in_descending_order_when_triduplicated() {
		MemTable.Add(0x01, 0x05, 0xfff9);
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x05, 0xfff5);
		MemTable.Add(0x11, 0x01, 0xfff2);

		var entries = MemTable.GetRange(0x11, 0x01, 0x01).ToArray();
		Assert.AreEqual(3, entries.Length);
		Assert.AreEqual(0x11, entries[0].Stream);
		Assert.AreEqual(0x01, entries[0].Version);
		Assert.AreEqual(0xfff3, entries[0].Position);

		Assert.AreEqual(0x11, entries[1].Stream);
		Assert.AreEqual(0x01, entries[1].Version);
		Assert.AreEqual(0xfff2, entries[1].Position);

		Assert.AreEqual(0x11, entries[2].Stream);
		Assert.AreEqual(0x01, entries[2].Version);
		Assert.AreEqual(0xfff1, entries[2].Position);
	}

	[Test]
	public void try_get_latest_entry_finds_nothing_on_empty_memtable() {
		IndexEntry entry;
		Assert.IsFalse(MemTable.TryGetLatestEntry(0x12, out entry));
	}

	[Test]
	public void try_get_latest_entry_finds_nothing_on_empty_stream() {
		MemTable.Add(0x11, 0x01, 0xffff);
		IndexEntry entry;
		Assert.IsFalse(MemTable.TryGetLatestEntry(0x12, out entry));
	}

	[Test]
	public void single_item_is_latest() {
		MemTable.Add(0x11, 0x01, 0xffff);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetLatestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x01, entry.Version);
		Assert.AreEqual(0xffff, entry.Position);
	}

	[Test]
	public void try_get_latest_entry_returns_correct_entry() {
		MemTable.Add(0x11, 0x01, 0xffff);
		MemTable.Add(0x11, 0x02, 0xfff2);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetLatestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x02, entry.Version);
		Assert.AreEqual(0xfff2, entry.Position);
	}

	[Test]
	public void try_get_latest_entry_when_duplicated_entries_returns_the_one_with_largest_position() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x02, 0xfff2);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff4);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetLatestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x02, entry.Version);
		Assert.AreEqual(0xfff4, entry.Position);
	}

	[Test]
	public void try_get_latest_entry_returns_the_entry_with_the_largest_position_when_triduplicated() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x01, 0xfff5);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetLatestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x01, entry.Version);
		Assert.AreEqual(0xfff5, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_finds_nothing_on_empty_memtable() {
		IndexEntry entry;
		Assert.IsFalse(MemTable.TryGetOldestEntry(0x12, out entry));
	}

	[Test]
	public void try_get_oldest_entry_finds_nothing_on_empty_stream() {
		MemTable.Add(0x11, 0x01, 0xffff);
		IndexEntry entry;
		Assert.IsFalse(MemTable.TryGetOldestEntry(0x12, out entry));
	}

	[Test]
	public void single_item_is_oldest() {
		MemTable.Add(0x11, 0x01, 0xffff);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetOldestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x01, entry.Version);
		Assert.AreEqual(0xffff, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_returns_correct_entry() {
		MemTable.Add(0x11, 0x01, 0xffff);
		MemTable.Add(0x11, 0x02, 0xfff2);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetOldestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x01, entry.Version);
		Assert.AreEqual(0xffff, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_when_duplicated_entries_returns_the_one_with_smallest_position() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x02, 0xfff2);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x02, 0xfff4);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetOldestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x01, entry.Version);
		Assert.AreEqual(0xfff1, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_returns_the_entry_with_the_smallest_position_when_triduplicated() {
		MemTable.Add(0x11, 0x01, 0xfff1);
		MemTable.Add(0x11, 0x01, 0xfff3);
		MemTable.Add(0x11, 0x01, 0xfff5);
		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetOldestEntry(0x11, out entry));
		Assert.AreEqual(0x11, entry.Stream);
		Assert.AreEqual(0x01, entry.Version);
		Assert.AreEqual(0xfff1, entry.Position);
	}


	[Test]
	public void the_smallest_items_with_hash_collisions_can_be_found() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		long position;
		Assert.IsTrue(MemTable.TryGetOneValue(0, 0, out position));
		Assert.AreEqual(0x0002, position);
	}

	[Test]
	public void the_smallest_items_with_hash_collisions_are_returned_in_descending_order() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		var entries = MemTable.GetRange(0, 0, 0).ToArray();
		Assert.AreEqual(2, entries.Length);
		Assert.AreEqual(0, entries[0].Stream);
		Assert.AreEqual(0, entries[0].Version);
		Assert.AreEqual(0x0002, entries[0].Position);
		Assert.AreEqual(0, entries[1].Stream);
		Assert.AreEqual(0, entries[1].Version);
		Assert.AreEqual(0x0001, entries[1].Position);
	}

	[Test]
	public void try_get_latest_entry_for_smallest_hash_with_collisions_returns_correct_index_entry() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetLatestEntry(0, out entry));
		Assert.AreEqual(0, entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0002, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_for_smallest_hash_with_collisions_returns_correct_index_entry() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetOldestEntry(0, out entry));
		Assert.AreEqual(0, entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0001, entry.Position);
	}

	[Test]
	public void the_largest_items_with_hash_collisions_can_be_found() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		long position;
		Assert.IsTrue(MemTable.TryGetOneValue(1, 0, out position));
		Assert.AreEqual(0x0005, position);
	}

	[Test]
	public void the_largest_items_with_hash_collisions_are_returned_in_descending_order() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		var entries = MemTable.GetRange(1, 0, 0).ToArray();
		Assert.AreEqual(3, entries.Length);
		Assert.AreEqual(1, entries[0].Stream);
		Assert.AreEqual(0, entries[0].Version);
		Assert.AreEqual(0x0005, entries[0].Position);
		Assert.AreEqual(1, entries[1].Stream);
		Assert.AreEqual(0, entries[1].Version);
		Assert.AreEqual(0x0004, entries[1].Position);
		Assert.AreEqual(1, entries[2].Stream);
		Assert.AreEqual(0, entries[2].Version);
		Assert.AreEqual(0x0003, entries[2].Position);
	}

	[Test]
	public void try_get_latest_entry_for_largest_hash_collision_returns_correct_index_entry() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetLatestEntry(1, out entry));
		Assert.AreEqual(1, entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0005, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_for_largest_hash_with_collisions_returns_correct_index_entry() {
		MemTable.Add(0, 0, 0x0001);
		MemTable.Add(0, 0, 0x0002);
		MemTable.Add(1, 0, 0x0003);
		MemTable.Add(1, 0, 0x0004);
		MemTable.Add(1, 0, 0x0005);

		IndexEntry entry;
		Assert.IsTrue(MemTable.TryGetOldestEntry(1, out entry));
		Assert.AreEqual(1, entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0003, entry.Position);
	}
}
