using System;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.Scavenge {
	[TestFixture(PTableVersions.IndexV2, false)]
	[TestFixture(PTableVersions.IndexV2, true)]
	[TestFixture(PTableVersions.IndexV3, false)]
	[TestFixture(PTableVersions.IndexV3, true)]
	[TestFixture(PTableVersions.IndexV4, false)]
	[TestFixture(PTableVersions.IndexV4, true)]
	public class when_scavenging_a_v1_index : SpecificationWithDirectoryPerTestFixture {
		private IHasher<string> hasher;

		private PTable _newtable;
		private readonly byte _newVersion;
		private bool _skipIndexVerify;
		private Func<string, ulong, ulong> _upgradeHash;
		private PTable _oldTable;

		public when_scavenging_a_v1_index(byte newVersion, bool skipIndexVerify) {
			_newVersion = newVersion;
			_skipIndexVerify = skipIndexVerify;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			hasher = new Murmur3AUnsafe();
			await base.TestFixtureSetUp();

			var table = new HashListMemTable(PTableVersions.IndexV1, maxSize: 20);
			table.Add(0x010100000000, 0, 1);
			table.Add(0x010200000000, 0, 2);
			table.Add(0x010300000000, 0, 3);
			table.Add(0x010300000000, 1, 4);
			_oldTable = PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault);

			long spaceSaved;
			_upgradeHash = (streamId, hash) => hash << 32 | hasher.Hash(streamId);
			Func<IndexEntry, bool> existsAt = x => x.Position % 2 == 0;
			Func<IndexEntry, Tuple<string, bool>> readRecord = x =>
				new Tuple<string, bool>(x.Stream.ToString(), x.Position % 2 == 0);
			_newtable = PTable.Scavenged(_oldTable, GetTempFilePath(), _upgradeHash, existsAt, readRecord, _newVersion,
				existsAt,
				out spaceSaved, skipIndexVerify: _skipIndexVerify, initialReaders: Constants.PTableInitialReaderCount, maxReaders: Constants.PTableMaxReaderCountDefault,
				useBloomFilter: true);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_oldTable.Dispose();
			_newtable.Dispose();

			return base.TestFixtureTearDown();
		}

		[Test]
		public void scavenged_ptable_is_new_version() {
			Assert.AreEqual(_newVersion, _newtable.Version);
		}

		[Test]
		public void there_are_2_records_in_the_merged_index() {
			Assert.AreEqual(2, _newtable.Count);
		}

		[Test]
		public void a_stream_can_be_found() {
			ulong entry1 = 0x0103;
			var stream = _upgradeHash($"{entry1}", entry1);
			Assert.True(_newtable.TryGetLatestEntry(stream, out var entry));
			Assert.AreEqual(stream, entry.Stream);
			Assert.AreEqual(1, entry.Version);
			Assert.AreEqual(4, entry.Position);
		}

		[Test]
		public void remaining_entries_should_have_been_upgraded_to_64bit_hash() {
			ulong entry1 = 0x0103;
			ulong entry2 = 0x0102;

			using (var enumerator = _newtable.IterateAllInOrder().GetEnumerator()) {
				Assert.That(enumerator.MoveNext());
				Assert.That(enumerator.Current.Stream, Is.EqualTo(_upgradeHash(entry1.ToString(), entry1)));

				Assert.That(enumerator.MoveNext());
				Assert.That(enumerator.Current.Stream, Is.EqualTo(_upgradeHash(entry2.ToString(), entry2)));
			}
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
