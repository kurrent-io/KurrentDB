// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class when_trying_to_get_latest_entry_before_position : SpecificationWithFile {
	private readonly byte _pTableVersion;
	private readonly bool _skipIndexVerify;

	private HashListMemTable _memTable;
	private PTable _pTable;
	private readonly long _deletedStreamEventNumber;

	private const ulong HNormal = 0x01UL << 32;
	private const ulong HTombstoned = 0x02UL << 32;
	private const ulong HDuplicate = 0x03UL << 32;
	private const ulong HOutOfOrder = 0x04UL << 32;
	private const ulong HNotExists = 0x05UL << 32;

	public when_trying_to_get_latest_entry_before_position(byte version, bool skipIndexVerify) {
		_pTableVersion = version;
		_skipIndexVerify = skipIndexVerify;
		_deletedStreamEventNumber = version < PTableVersions.IndexV3 ? int.MaxValue : long.MaxValue;
	}

	private ulong GetHash(ulong value) {
		return _pTableVersion == PTableVersions.IndexV1 ? value >> 32 : value;
	}

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		_memTable = new HashListMemTable(_pTableVersion, maxSize: 10);
		_memTable.Add(HNormal, 0, 0);
		_memTable.Add(HNormal, 1, 1);
		_memTable.Add(HNormal, 2, 2);
		_memTable.Add(HTombstoned, 1, 3);
		_memTable.Add(HNormal, 5, 4);
		_memTable.Add(HTombstoned, _deletedStreamEventNumber, 5);
		_memTable.Add(HDuplicate, 0, 6);
		_memTable.Add(HDuplicate, 0, 7);
		_memTable.Add(HDuplicate, 1, 8);
		_memTable.Add(HDuplicate, 1, 9);
		_memTable.Add(HOutOfOrder, 0, 10);
		_memTable.Add(HOutOfOrder, 2, 11);
		_memTable.Add(HOutOfOrder, 1, 12);
		_pTable = PTable.FromMemtable(
			table: _memTable,
			filename: Filename,
			initialReaders: Constants.PTableInitialReaderCount,
			maxReaders: Constants.PTableMaxReaderCountDefault,
			skipIndexVerify: _skipIndexVerify);
	}

	[TearDown]
	public override void TearDown() {
		_pTable?.Dispose();
		base.TearDown();
	}

	private ISearchTable GetTable(bool memTableOrPTable) => memTableOrPTable ? (ISearchTable) _memTable : _pTable;

	[TestCase(true)]
	[TestCase(false)]
	public async Task when_hash_doesnt_exist_returns_false(bool memTableOrPTable) {
		var table = GetTable(memTableOrPTable);
		Assert.Null(await table.TryGetLatestEntry(HNotExists, 10, (x, token) => {
			Assert.AreEqual(GetHash(HNotExists), x.Stream);
			return new(true);
		}, CancellationToken.None));
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task when_hash_exists_but_not_before_position_limit_returns_false(bool memTableOrPTable) {
		var table = GetTable(memTableOrPTable);
		Assert.Null(await table.TryGetLatestEntry(HNormal, 0, (x, token) => {
			Assert.AreEqual(GetHash(HNormal), x.Stream);
			return new(true);
		}, CancellationToken.None));

		Assert.Null(await table.TryGetLatestEntry(HTombstoned, 3, (x, token) => {
			Assert.AreEqual(GetHash(HTombstoned), x.Stream);
			return new(true);
		}, CancellationToken.None));

		Assert.Null(await table.TryGetLatestEntry(HDuplicate, 6, (x, token) => {
			Assert.AreEqual(GetHash(HDuplicate), x.Stream);
			return new(true);
		}, CancellationToken.None));
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task when_hash_exists_before_position_limit_returns_correct_entry(bool memTableOrPTable) {
		var table = GetTable(memTableOrPTable);
		var res = await table.TryGetLatestEntry(HNormal, 5, (x, token) => {
			Assert.AreEqual(GetHash(HNormal), x.Stream);
			return new(true);
		}, CancellationToken.None);
		Assert.True(res.HasValue);
		Assert.AreEqual(4, res.GetValueOrDefault().Position);
		Assert.AreEqual(GetHash(HNormal), res.GetValueOrDefault().Stream);
		Assert.AreEqual(5, res.GetValueOrDefault().Version);

		res = await table.TryGetLatestEntry(HTombstoned, 5, (x, token) => {
			Assert.AreEqual(GetHash(HTombstoned), x.Stream);
			return new(true);
		}, CancellationToken.None);
		Assert.True(res.HasValue);
		Assert.AreEqual(3, res.GetValueOrDefault().Position);
		Assert.AreEqual(GetHash(HTombstoned), res.GetValueOrDefault().Stream);
		Assert.AreEqual(1, res.GetValueOrDefault().Version);

		res = await table.TryGetLatestEntry(HDuplicate, 7, (x, token) => {
			Assert.AreEqual(GetHash(HDuplicate), x.Stream);
			return new(true);
		}, CancellationToken.None);
		Assert.True(res.HasValue);
		Assert.AreEqual(6, res.GetValueOrDefault().Position);
		Assert.AreEqual(GetHash(HDuplicate), res.GetValueOrDefault().Stream);
		Assert.AreEqual(0, res.GetValueOrDefault().Version);
	}

	[TestCase(true)]
	[TestCase(false)]
	// at the moment, this TryGetLatestEntry overload is used only for scavenging purposes
	// and we are only interested with finding the latest event number before a position limit,
	// not the actual index entry
	public async Task when_duplicate_returns_entry_with_highest_position(bool memTableOrPTable) {
		var table = GetTable(memTableOrPTable);

		var res = await table.TryGetLatestEntry(HDuplicate, 8, (x, token) => {
			Assert.AreEqual(GetHash(HDuplicate), x.Stream);
			return new(true);
		}, CancellationToken.None);
		Assert.True(res.HasValue);
		Assert.AreEqual(7, res.GetValueOrDefault().Position);
		Assert.AreEqual(GetHash(HDuplicate), res.GetValueOrDefault().Stream);
		Assert.AreEqual(0, res.GetValueOrDefault().Version);
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task when_hash_collision_returns_correct_result(bool memTableOrPTable) {
		var table = GetTable(memTableOrPTable);

		var res = await table.TryGetLatestEntry(HNormal, 10,
			(x, token) => {
				Assert.AreEqual(GetHash(HNormal), x.Stream);
				return new(x.Position % 2 is 1);
			}, CancellationToken.None);
		Assert.True(res.HasValue);
		Assert.AreEqual(1, res.GetValueOrDefault().Position);
		Assert.AreEqual(GetHash(HNormal), res.GetValueOrDefault().Stream);
		Assert.AreEqual(1, res.GetValueOrDefault().Version);

		Assert.Null(await table.TryGetLatestEntry(HNormal, 10, (x, token) => {
			Assert.AreEqual(GetHash(HNormal), x.Stream);
			return new(false);
		}, CancellationToken.None));
	}

	[TestCase(true)]
	[TestCase(false)]
	// at the moment, this TryGetLatestEntry overload is used only for scavenging purposes.
	// if an out of order event occurs in the log (due to a bug), the function may return
	// an index entry with an event number less than the real last event number.
	// however, the consequences are not so bad: it can only result in less events
	// being scavenged. during index execution, detection of these out of order events
	// in PTables is done and logged.
	public async Task when_out_of_order_may_return_incorrect_entry_with_smaller_event_number(bool memTableOrPTable) {
		var table = GetTable(memTableOrPTable);
		var res = await table.TryGetLatestEntry(HOutOfOrder, 12, (x, token) => {
			Assert.AreEqual(GetHash(HOutOfOrder), x.Stream);
			return new(true);
		}, CancellationToken.None);
		Assert.True(res.HasValue);
		Assert.AreEqual(GetHash(HOutOfOrder), res.GetValueOrDefault().Stream);

		// correct last event number should be 2
		Assert.AreEqual(0, res.GetValueOrDefault().Version);
	}
}
