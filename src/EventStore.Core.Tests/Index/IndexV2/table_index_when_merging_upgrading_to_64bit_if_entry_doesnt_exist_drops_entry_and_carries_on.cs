using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Tests.Services.Storage;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using LogV3StreamId = System.UInt32;

namespace EventStore.Core.Tests.Index.IndexV2 {
	public class TestCases : IEnumerable {
		public IEnumerator GetEnumerator() {
			yield return new object[] { typeof(LogFormat.V2), typeof(string), new ByLengthHasher(), new ByLengthHasher(), "hhh", "hh", "h" };
			yield return new object[] { typeof(LogFormat.V3), typeof(LogV3StreamId), new IdentityLowHasher(), new IdentityHighHasher(), 1028U, 1026U, 1024U };
		}
	}

	[Category("LongRunning")]
	[TestFixture, TestFixtureSource(typeof(TestCases))]
	public class
		table_index_when_merging_upgrading_to_64bit_if_single_stream_entry_doesnt_exist_drops_entry_and_carries_on<TLogFormat, TStreamId> :
			SpecificationWithDirectoryPerTestFixture {
		private TableIndex<TStreamId> _tableIndex;
		private IHasher<TStreamId> _lowHasher;
		private IHasher<TStreamId> _highHasher;
		private string _indexDir;
		protected byte _ptableVersion;

		// Note hash is by length so stream ids are set to order them specifically in the index.
		private static TStreamId _streamId1;
		private static TStreamId _streamId2;
		private static TStreamId _streamId3;

		public
			table_index_when_merging_upgrading_to_64bit_if_single_stream_entry_doesnt_exist_drops_entry_and_carries_on(
				IHasher<TStreamId> lowHasher,
				IHasher<TStreamId> highHasher,
				TStreamId streamId1,
				TStreamId streamId2,
				TStreamId streamId3) {
			_ptableVersion = PTableVersions.IndexV2;
			_lowHasher = lowHasher;
			_highHasher = highHasher;
			_streamId1 = streamId1;
			_streamId2 = streamId2;
			_streamId3 = streamId3;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {
			await base.TestFixtureSetUp();

			var emptyStreamId = LogFormatHelper<TLogFormat, TStreamId>.EmptyStreamId;
			_indexDir = PathName;
			var fakeReader = new TFReaderLease(new FakeIndexReader2());
			_tableIndex = new TableIndex<TStreamId>(_indexDir, _lowHasher, _highHasher, emptyStreamId,
				() => new HashListMemTable(PTableVersions.IndexV1, maxSize: 3),
				() => fakeReader,
				PTableVersions.IndexV1,
				5, Constants.PTableMaxReaderCountDefault,
				maxSizeForMemory: 3,
				maxTablesPerLevel: 2);
			_tableIndex.Initialize(long.MaxValue);

			_tableIndex.Add(1, _streamId1, 0, 1);
			_tableIndex.Add(1, _streamId2, 0, 2);
			_tableIndex.Add(1, _streamId3, 0, 3);

			_tableIndex.Close(false);

			_tableIndex = new TableIndex<TStreamId>(_indexDir, _lowHasher, _highHasher, emptyStreamId,
				() => new HashListMemTable(_ptableVersion, maxSize: 3),
				() => fakeReader,
				_ptableVersion,
				5, Constants.PTableMaxReaderCountDefault,
				maxSizeForMemory: 3,
				maxTablesPerLevel: 2);
			_tableIndex.Initialize(long.MaxValue);

			_tableIndex.Add(1, _streamId3, 1, 4);
			_tableIndex.Add(1, _streamId2, 1, 5);
			_tableIndex.Add(1, _streamId1, 1, 6);

			await Task.Delay(500);
		}

		[OneTimeTearDown]
		public override Task TestFixtureTearDown() {
			_tableIndex.Close();

			return base.TestFixtureTearDown();
		}

		[Test]
		public void should_have_all_entries_except_scavenged() {
			var streamId = _streamId1;
			var result = _tableIndex.GetRange(streamId, 0, 1).ToArray();
			var hash = (ulong)_lowHasher.Hash(streamId) << 32 | _highHasher.Hash(streamId);

			Assert.That(result.Count(), Is.EqualTo(1));

			Assert.That(result[0].Stream, Is.EqualTo(hash));
			Assert.That(result[0].Version, Is.EqualTo(1));
			Assert.That(result[0].Position, Is.EqualTo(6));

			streamId = _streamId2;
			result = _tableIndex.GetRange(streamId, 0, 1).ToArray();
			hash = (ulong)_lowHasher.Hash(streamId) << 32 | _highHasher.Hash(streamId);

			Assert.That(result.Count(), Is.EqualTo(1));

			Assert.That(result[0].Stream, Is.EqualTo(hash));
			Assert.That(result[0].Version, Is.EqualTo(1));
			Assert.That(result[0].Position, Is.EqualTo(5));

			streamId = _streamId3;
			result = _tableIndex.GetRange(streamId, 0, 1).ToArray();
			hash = (ulong)_lowHasher.Hash(streamId) << 32 | _highHasher.Hash(streamId);

			Assert.That(result.Count(), Is.EqualTo(2));

			Assert.That(result[0].Stream, Is.EqualTo(hash));
			Assert.That(result[0].Version, Is.EqualTo(1));
			Assert.That(result[0].Position, Is.EqualTo(4));

			Assert.That(result[1].Stream, Is.EqualTo(hash));
			Assert.That(result[1].Version, Is.EqualTo(0));
			Assert.That(result[1].Position, Is.EqualTo(3));
		}

		private class FakeIndexReader2 : ITransactionFileReader {
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
				TStreamId streamId;
				switch (position) {
					case 1:
						streamId = _streamId1;
						break;
					case 2:
						streamId = _streamId2;
						break;
					case 3:
						streamId = _streamId3;
						break;
					default:
						throw new ArgumentOutOfRangeException("Unexpected position look up.");
				}

				var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
				var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
				var record = LogRecord.Prepare(recordFactory, position, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
					streamId, -1, PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
				return new RecordReadResult(true, position + 1, record, 1);
			}

			public bool ExistsAt(long position) {
				return position != 2 && position != 1;
			}
		}
	}
}
