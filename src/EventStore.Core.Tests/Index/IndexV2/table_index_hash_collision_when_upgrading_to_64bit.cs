using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV2 {
	[TestFixture(0, 0)]
	[TestFixture(10, 0)]
	[TestFixture(0, 10)]
	[TestFixture(10, 10)]
	public class table_index_hash_collision_when_upgrading_to_64bit : SpecificationWithDirectoryPerTestFixture {
		private TableIndex<string> _tableIndex;
		private IHasher<string> _lowHasher;
		private IHasher<string> _highHasher;
		private string _indexDir;
		protected byte _ptableVersion;
		private int _extraStreamHashesAtBeginning;
		private int _extraStreamHashesAtEnd;

		public table_index_hash_collision_when_upgrading_to_64bit(int extraStreamHashesAtBeginning,
			int extraStreamHashesAtEnd) : base() {
			_ptableVersion = PTableVersions.IndexV2;
			_extraStreamHashesAtBeginning = extraStreamHashesAtBeginning;
			_extraStreamHashesAtEnd = extraStreamHashesAtEnd;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			await base.TestFixtureSetUp();

			_indexDir = PathName;
			var fakeReader = new TFReaderLease(new FakeIndexReader());
			_lowHasher = new XXHashUnsafe();
			_highHasher = new Murmur3AUnsafe();
			_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, "",
				() => new HashListMemTable(PTableVersions.IndexV1, maxSize: 5),
				() => fakeReader,
				PTableVersions.IndexV1,
				5, Constants.PTableMaxReaderCountDefault,
				maxSizeForMemory: 5 + _extraStreamHashesAtBeginning + _extraStreamHashesAtEnd,
				maxTablesPerLevel: 2);
			_tableIndex.Initialize(long.MaxValue);

			Assert.Greater(_lowHasher.Hash("abcd"), _lowHasher.Hash("LPN-FC002_LPK51001"));
			for (int i = 0; i < _extraStreamHashesAtBeginning; i++) {
				_tableIndex.Add(1, "abcd", i, i + 1);
			}

			Assert.Less(_lowHasher.Hash("wxyz"), _lowHasher.Hash("LPN-FC002_LPK51001"));
			for (int i = 0; i < _extraStreamHashesAtEnd; i++) {
				_tableIndex.Add(1, "wxyz", i, i + 1);
			}

			_tableIndex.Add(1, "LPN-FC002_LPK51001", 0, 1);
			_tableIndex.Add(1, "account--696193173", 0, 2);
			_tableIndex.Add(1, "LPN-FC002_LPK51001", 1, 3);
			_tableIndex.Add(1, "account--696193173", 1, 4);
			_tableIndex.Add(1, "LPN-FC002_LPK51001", 2, 5);

			_tableIndex.Close(false);

			_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, "",
				() => new HashListMemTable(_ptableVersion, maxSize: 5),
				() => fakeReader,
				_ptableVersion,
				5, Constants.PTableMaxReaderCountDefault,
				maxSizeForMemory: 5,
				maxTablesPerLevel: 2);
			_tableIndex.Initialize(long.MaxValue);

			_tableIndex.Add(1, "account--696193173", 2, 6);
			_tableIndex.Add(1, "LPN-FC002_LPK51001", 3, 7);
			_tableIndex.Add(1, "account--696193173", 3, 8);
			_tableIndex.Add(1, "LPN-FC002_LPK51001", 4, 9);
			_tableIndex.Add(1, "account--696193173", 4, 10);

			await Task.Delay(500);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_tableIndex.Close();

			return base.TestFixtureTearDown();
		}

		[Test]
		public void should_have_entries_in_sorted_order() {
			var streamId = "account--696193173";
			var result = _tableIndex.GetRange(streamId, 0, 4).ToArray();
			var hash = (ulong)_lowHasher.Hash(streamId) << 32 | _highHasher.Hash(streamId);

			Assert.That(result.Count(), Is.EqualTo(5));

			Assert.That(result[0].Stream, Is.EqualTo(hash));
			Assert.That(result[0].Version, Is.EqualTo(4));
			Assert.That(result[0].Position, Is.EqualTo(10));

			Assert.That(result[1].Stream, Is.EqualTo(hash));
			Assert.That(result[1].Version, Is.EqualTo(3));
			Assert.That(result[1].Position, Is.EqualTo(8));

			Assert.That(result[2].Stream, Is.EqualTo(hash));
			Assert.That(result[2].Version, Is.EqualTo(2));
			Assert.That(result[2].Position, Is.EqualTo(6));

			Assert.That(result[3].Stream, Is.EqualTo(hash));
			Assert.That(result[3].Version, Is.EqualTo(1));
			Assert.That(result[3].Position, Is.EqualTo(4));

			Assert.That(result[4].Stream, Is.EqualTo(hash));
			Assert.That(result[4].Version, Is.EqualTo(0));
			Assert.That(result[4].Position, Is.EqualTo(2));

			streamId = "LPN-FC002_LPK51001";
			result = _tableIndex.GetRange(streamId, 0, 4).ToArray();
			hash = (ulong)_lowHasher.Hash(streamId) << 32 | _highHasher.Hash(streamId);

			Assert.That(result.Count(), Is.EqualTo(5));

			Assert.That(result[0].Stream, Is.EqualTo(hash));
			Assert.That(result[0].Version, Is.EqualTo(4));
			Assert.That(result[0].Position, Is.EqualTo(9));

			Assert.That(result[1].Stream, Is.EqualTo(hash));
			Assert.That(result[1].Version, Is.EqualTo(3));
			Assert.That(result[1].Position, Is.EqualTo(7));

			Assert.That(result[2].Stream, Is.EqualTo(hash));
			Assert.That(result[2].Version, Is.EqualTo(2));
			Assert.That(result[2].Position, Is.EqualTo(5));

			Assert.That(result[3].Stream, Is.EqualTo(hash));
			Assert.That(result[3].Version, Is.EqualTo(1));
			Assert.That(result[3].Position, Is.EqualTo(3));

			Assert.That(result[4].Stream, Is.EqualTo(hash));
			Assert.That(result[4].Version, Is.EqualTo(0));
			Assert.That(result[4].Position, Is.EqualTo(1));
		}
	}

	public class FakeIndexReader : ITransactionFileReader {
		public void Reposition(long position) {
			throw new NotImplementedException();
		}

		public SeqReadResult TryReadNext() {
			throw new NotImplementedException();
		}

		public SeqReadResult TryReadPrev() {
			throw new NotImplementedException();
		}

		public RecordReadResult TryReadAt(long position, bool couldBeScavenged) {
			var record = (LogRecord)new PrepareLogRecord(position, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
				position % 2 == 0 ? "account--696193173" : "LPN-FC002_LPK51001", null, -1, DateTime.UtcNow, PrepareFlags.None,
				"type", null, new byte[0], null);
			return new RecordReadResult(true, position + 1, record, 1);
		}

		public bool ExistsAt(long position) {
			return true;
		}
	}
}
