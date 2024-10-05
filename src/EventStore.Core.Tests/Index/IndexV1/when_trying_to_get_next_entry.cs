// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1 {
	[TestFixture(PTableVersions.IndexV2, false)]
	[TestFixture(PTableVersions.IndexV2, true)]
	[TestFixture(PTableVersions.IndexV3, false)]
	[TestFixture(PTableVersions.IndexV3, true)]
	[TestFixture(PTableVersions.IndexV4, false)]
	[TestFixture(PTableVersions.IndexV4, true)]
	public class when_trying_to_get_next_entry : SpecificationWithFile {
		private readonly byte _pTableVersion;
		private readonly bool _skipIndexVerify;

		private HashListMemTable _memTable;
		private PTable _pTable;
		private readonly long _deletedStreamEventNumber;

		private const ulong H1 = 0x01UL << 32;
		private const ulong H2 = 0x02UL << 32;
		private const ulong H3 = 0x03UL << 32;

		public when_trying_to_get_next_entry(byte version, bool skipIndexVerify) {
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
			_memTable.Add(H1, 0, 0);
			_memTable.Add(H1, 1, 1);
			_memTable.Add(H1, 2, 2);
			_memTable.Add(H2, 1, 3);
			_memTable.Add(H1, 5, 4);
			_memTable.Add(H2, _deletedStreamEventNumber, 5);
			_memTable.Add(H3, 0, 6);
			_memTable.Add(H3, 0, 7);
			_memTable.Add(H3, 1, 8);
			_memTable.Add(H3, 1, 9);
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
		public void when_next_entry_doesnt_exist_returns_false(bool memTableOrPTable) {
			var table = GetTable(memTableOrPTable);
			Assert.False(table.TryGetNextEntry(H1, 5, out _));
			Assert.False(table.TryGetNextEntry(H2, _deletedStreamEventNumber, out _));
		}

		[TestCase(true)]
		[TestCase(false)]
		public void when_next_entry_exists_returns_correct_entry(bool memTableOrPTable) {
			var table = GetTable(memTableOrPTable);

			Assert.True(table.TryGetNextEntry(H1, 0, out var entry));
			Assert.AreEqual(GetHash(H1), entry.Stream);
			Assert.AreEqual(1, entry.Version);

			Assert.True(table.TryGetNextEntry(H1, 1, out entry));
			Assert.AreEqual(GetHash(H1), entry.Stream);
			Assert.AreEqual(2, entry.Version);

			Assert.True(table.TryGetNextEntry(H1, 2, out entry));
			Assert.AreEqual(GetHash(H1), entry.Stream);
			Assert.AreEqual(5, entry.Version);

			Assert.True(table.TryGetNextEntry(H2, 1, out entry));
			Assert.AreEqual(GetHash(H2), entry.Stream);
			Assert.AreEqual(_deletedStreamEventNumber, entry.Version);
		}

		[TestCase(true)]
		[TestCase(false)]
		public void when_duplicate_or_collision_returns_correct_next_entry(bool memTableOrPTable) {
			var table = GetTable(memTableOrPTable);

			Assert.True(table.TryGetNextEntry(H3, 0, out var entry));
			Assert.AreEqual(GetHash(H3), entry.Stream);
			Assert.AreEqual(1, entry.Version);
		}
	}
}
