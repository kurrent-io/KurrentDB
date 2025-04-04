using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1 {
	[TestFixture(false)]
	[TestFixture(true)]
	public class when_merging_ptables : SpecificationWithDirectoryPerTestFixture {
		private readonly List<string> _files = new List<string>();
		private readonly List<PTable> _tables = new List<PTable>();

		private PTable _newtable;
		private bool _skipIndexVerify;

		public when_merging_ptables(bool skipIndexVerify) {
			_skipIndexVerify = skipIndexVerify;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			await base.TestFixtureSetUp();
			_files.Add(GetTempFilePath());
			var table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010100000000, 0, 0x0101);
			table.Add(0x010200000000, 0, 0x0102);
			table.Add(0x010300000000, 0, 0x0103);
			table.Add(0x010400000000, 0, 0x0104);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010500000000, 0, 0x0105);
			table.Add(0x010600000000, 0, 0x0106);
			table.Add(0x010700000000, 0, 0x0107);
			table.Add(0x010800000000, 0, 0x0108);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			_newtable = PTable.MergeTo(_tables, GetTempFilePath(), (streamId, hash) => hash + 1, x => true,
				x => new Tuple<string, bool>(x.Stream.ToString(), true), PTableVersions.IndexV1, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
				skipIndexVerify: _skipIndexVerify,
				useBloomFilter: true);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_newtable.Dispose();
			foreach (var ssTable in _tables) {
				ssTable.Dispose();
			}

			return base.TestFixtureTearDown();
		}

		[Test]
		public void merged_ptable_is_32bit() {
			Assert.AreEqual(PTableVersions.IndexV1, _newtable.Version);
		}

		[Test]
		public void there_are_8_records_in_the_merged_index() {
			Assert.AreEqual(8, _newtable.Count);
		}

		[Test]
		public void a_stream_can_be_found() {
			Assert.True(_newtable.TryGetLatestEntry(0x010800000000, out var entry));
			Assert.AreEqual(0x0108, entry.Stream);
			Assert.AreEqual(0, entry.Version);
			Assert.AreEqual(0x0108, entry.Position);
		}

		[Test]
		public void no_entries_should_have_upgraded_hashes() {
			foreach (var item in _newtable.IterateAllInOrder()) {
				Assert.IsTrue((ulong)item.Position == item.Stream, "Expected the Stream (Hash) {0} to be equal to {1}",
					item.Stream, item.Position);
			}
		}
	}

	[TestFixture(false)]
	[TestFixture(true)]
	public class when_merging_ptables_to_64bit : SpecificationWithDirectoryPerTestFixture {
		private readonly List<string> _files = new List<string>();
		private readonly List<PTable> _tables = new List<PTable>();

		private PTable _newtable;
		private bool _skipIndexVerify;

		public when_merging_ptables_to_64bit(bool skipIndexVerify) {
			_skipIndexVerify = skipIndexVerify;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			await base.TestFixtureSetUp();
			_files.Add(GetTempFilePath());
			var table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010100000000, 0, 0x0101);
			table.Add(0x010200000000, 0, 0x0102);
			table.Add(0x010300000000, 0, 0x0103);
			table.Add(0x010400000000, 0, 0x0104);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010500000000, 0, 0x0105);
			table.Add(0x010600000000, 0, 0x0106);
			table.Add(0x010700000000, 0, 0x0107);
			table.Add(0x010800000000, 0, 0x0108);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			_newtable = PTable.MergeTo(_tables, GetTempFilePath(), (streamId, hash) => hash + 1, x => true,
				x => new Tuple<string, bool>(x.Stream.ToString(), true), PTableVersions.IndexV3, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
				skipIndexVerify: _skipIndexVerify,
				useBloomFilter: true);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_newtable.Dispose();
			foreach (var ssTable in _tables) {
				ssTable.Dispose();
			}

			return base.TestFixtureTearDown();
		}

		[Test]
		public void merged_ptable_is_64bit() {
			Assert.AreEqual(PTableVersions.IndexV3, _newtable.Version);
		}

		[Test]
		public void there_are_8_records_in_the_merged_index() {
			Assert.AreEqual(8, _newtable.Count);
		}

		[Test]
		public void a_stream_can_be_found() {
			var stream = (ulong)0x0109;
			Assert.True(_newtable.TryGetLatestEntry(stream, out var entry));
			Assert.AreEqual(stream, entry.Stream);
			Assert.AreEqual(0, entry.Version);
			Assert.AreEqual(0x0108, entry.Position);
		}

		[Test]
		public void all_the_entries_have_upgraded_hashes() {
			foreach (var item in _newtable.IterateAllInOrder()) {
				Assert.IsTrue((ulong)item.Position == item.Stream - 1,
					"Expected the Stream (Hash) {0} to be equal to {1}", item.Stream - 1, item.Position);
			}
		}
	}

	[TestFixture(false)]
	[TestFixture(true)]
	public class when_merging_2_32bit_ptables_and_1_64bit_ptable_to_64bit : SpecificationWithDirectoryPerTestFixture {
		private readonly List<string> _files = new List<string>();
		private readonly List<PTable> _tables = new List<PTable>();

		private PTable _newtable;
		private bool _skipIndexVerify;

		public when_merging_2_32bit_ptables_and_1_64bit_ptable_to_64bit(bool skipIndexVerify) {
			_skipIndexVerify = skipIndexVerify;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			await base.TestFixtureSetUp();
			_files.Add(GetTempFilePath());
			var table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010100000000, 0, 0x010100000000);
			table.Add(0x010200000000, 0, 0x010200000000);
			table.Add(0x010300000000, 0, 0x010300000000);
			table.Add(0x010400000000, 0, 0x010400000000);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010500000000, 0, 0x010500000000);
			table.Add(0x010600000000, 0, 0x010600000000);
			table.Add(0x010700000000, 0, 0x010700000000);
			table.Add(0x010800000000, 0, 0x010800000000);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			table = new HashListMemTable(PTableVersions.IndexV2, maxSize: 20);
			table.Add(0x010900000000, 0, 0x010900000000);
			table.Add(0x101000000000, 0, 0x101000000000);
			table.Add(0x111000000000, 0, 0x111000000000);
			table.Add(0x121000000000, 0, 0x121000000000);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			_newtable = PTable.MergeTo(_tables, GetTempFilePath(), (streamId, hash) => hash + 1, x => true,
				x => new Tuple<string, bool>(x.Stream.ToString(), true), PTableVersions.IndexV2, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
				skipIndexVerify: _skipIndexVerify,
				useBloomFilter: true);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_newtable.Dispose();
			foreach (var ssTable in _tables) {
				ssTable.Dispose();
			}

			return base.TestFixtureTearDown();
		}

		[Test]
		public void merged_ptable_is_64bit() {
			Assert.AreEqual(PTableVersions.IndexV2, _newtable.Version);
		}

		[Test]
		public void there_are_12_records_in_the_merged_index() {
			Assert.AreEqual(12, _newtable.Count);
		}

		[Test]
		public void a_stream_can_be_found() {
			var stream = (ulong)0x121000000000;
			Assert.True(_newtable.TryGetLatestEntry(stream, out var entry));
			Assert.AreEqual(stream, entry.Stream);
			Assert.AreEqual(0, entry.Version);
			Assert.AreEqual(0x121000000000, entry.Position);
		}

		[Test]
		public void only_the_32_bit_index_entries_should_have_upgraded_hashes() {
			foreach (var item in _newtable.IterateAllInOrder()) {
				if (item.Position >= 0x010900000000) //these are 64bit already
				{
					Assert.IsTrue((ulong)item.Position == item.Stream,
						"Expected the Stream (Hash) {0} to be equal to {1}", item.Stream, item.Position);
				} else {
					Assert.IsTrue((ulong)item.Position >> 32 == item.Stream - 1,
						"Expected the Stream (Hash) {0} to be equal to {1}", item.Stream - 1, item.Position);
				}
			}
		}
	}

	[TestFixture(false)]
	[TestFixture(true)]
	public class
		when_merging_1_32bit_ptables_and_1_64bit_ptable_with_missing_entries_to_64bit :
			SpecificationWithDirectoryPerTestFixture {
		private readonly List<string> _files = new List<string>();
		private readonly List<PTable> _tables = new List<PTable>();
		private IHasher<string> hasher;

		private PTable _newtable;
		private bool _skipIndexVerify;

		public when_merging_1_32bit_ptables_and_1_64bit_ptable_with_missing_entries_to_64bit(bool skipIndexVerify) {
			_skipIndexVerify = skipIndexVerify;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			hasher = new Murmur3AUnsafe();
			await base.TestFixtureSetUp();
			_files.Add(GetTempFilePath());
			var table = new HashListMemTable(PTableVersions.IndexV2, maxSize: 20);
			table.Add(0x010100000000, 2, 5);
			table.Add(0x010200000000, 1, 6);
			table.Add(0x010200000000, 2, 7);
			table.Add(0x010400000000, 0, 8);
			table.Add(0x010400000000, 1, 9);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010100000000, 1, 10);
			table.Add(0x010100000000, 2, 11);
			table.Add(0x010500000000, 1, 12);
			table.Add(0x010500000000, 2, 13);
			table.Add(0x010500000000, 3, 14);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			_newtable = PTable.MergeTo(_tables, GetTempFilePath(),
				(streamId, hash) => hash << 32 | hasher.Hash(streamId), x => x.Position % 2 == 0,
				x => new Tuple<string, bool>(x.Stream.ToString(), x.Position % 2 == 0), PTableVersions.IndexV2, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
				skipIndexVerify: _skipIndexVerify,
				useBloomFilter: true);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_newtable.Dispose();
			foreach (var ssTable in _tables) {
				ssTable.Dispose();
			}

			return base.TestFixtureTearDown();
		}

		[Test]
		public void merged_ptable_is_64bit() {
			Assert.AreEqual(PTableVersions.IndexV2, _newtable.Version);
		}

		[Test]
		public void there_are_8_records_in_the_merged_index() {
			// 5 from the 64 bit table (existsAt doesn't get used)
			// 3 from the 32 bit table (3 even positions)
			Assert.AreEqual(8, _newtable.Count);
		}

		[Test]
		public void a_stream_can_be_found() {
			var stream = (ulong)0x010400000000;
			Assert.True(_newtable.TryGetLatestEntry(stream, out var entry));
			Assert.AreEqual(stream, entry.Stream);
			Assert.AreEqual(1, entry.Version);
			Assert.AreEqual(9, entry.Position);
		}

		[Test]
		public void the_items_are_sorted() {
			var last = new IndexEntry(ulong.MaxValue, 0, long.MaxValue);
			foreach (var item in _newtable.IterateAllInOrder()) {
				Assert.IsTrue((last.Stream == item.Stream ? last.Version > item.Version : last.Stream > item.Stream) ||
							  ((last.Stream == item.Stream && last.Version == item.Version) &&
							   last.Position > item.Position));
				last = item;
			}
		}
	}

	[TestFixture(false)]
	[TestFixture(true)]
	public class
		when_merging_2_32bit_ptables_and_1_64bit_ptable_with_missing_entries_to_64bit :
			SpecificationWithDirectoryPerTestFixture {
		private readonly List<string> _files = new List<string>();
		private readonly List<PTable> _tables = new List<PTable>();
		private IHasher<string> hasher;

		private PTable _newtable;
		private bool _skipIndexVerify;

		public when_merging_2_32bit_ptables_and_1_64bit_ptable_with_missing_entries_to_64bit(bool skipIndexVerify) {
			_skipIndexVerify = skipIndexVerify;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			hasher = new Murmur3AUnsafe();
			await base.TestFixtureSetUp();
			_files.Add(GetTempFilePath());
			var table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010100000000, 0, 1);
			table.Add(0x010200000000, 0, 2);
			table.Add(0x010300000000, 0, 3);
			table.Add(0x010300000000, 1, 4);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			table = new HashListMemTable(PTableVersions.IndexV2, maxSize: 20);
			table.Add(0x010100000000, 2, 5);
			table.Add(0x010200000000, 1, 6);
			table.Add(0x010200000000, 2, 7);
			table.Add(0x010400000000, 0, 8);
			table.Add(0x010400000000, 1, 9);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010100000000, 1, 10);
			table.Add(0x010100000000, 2, 11);
			table.Add(0x010500000000, 1, 12);
			table.Add(0x010500000000, 2, 13);
			table.Add(0x010500000000, 3, 14);
			_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
			_newtable = PTable.MergeTo(_tables, GetTempFilePath(),
				(streamId, hash) => hash << 32 | hasher.Hash(streamId), x => x.Position % 2 == 0,
				x => new Tuple<string, bool>(x.Stream.ToString(), x.Position % 2 == 0), PTableVersions.IndexV2, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
				skipIndexVerify: _skipIndexVerify,
				useBloomFilter: true);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_newtable.Dispose();
			foreach (var ssTable in _tables) {
				ssTable.Dispose();
			}

			return base.TestFixtureTearDown();
		}

		[Test]
		public void merged_ptable_is_64bit() {
			Assert.AreEqual(PTableVersions.IndexV2, _newtable.Version);
		}

		[Test]
		public void there_are_10_records_in_the_merged_index() {
			// 5 from 64 bit (existsAt not called)
			// 2 from first table (2 even positions)
			// 3 from last table (3 even positions)
			Assert.AreEqual(10, _newtable.Count);
		}

		[Test]
		public void a_stream_can_be_found() {
			var stream = (ulong)0x010400000000;
			Assert.True(_newtable.TryGetLatestEntry(stream, out var entry));
			Assert.AreEqual(stream, entry.Stream);
			Assert.AreEqual(1, entry.Version);
			Assert.AreEqual(9, entry.Position);
		}

		[Test]
		public void the_items_are_sorted() {
			var last = new IndexEntry(ulong.MaxValue, 0, long.MaxValue);
			foreach (var item in _newtable.IterateAllInOrder()) {
				Assert.IsTrue((last.Stream == item.Stream ? last.Version > item.Version : last.Stream > item.Stream) ||
							  ((last.Stream == item.Stream && last.Version == item.Version) &&
							   last.Position > item.Position));
				last = item;
			}
		}
	}
}
