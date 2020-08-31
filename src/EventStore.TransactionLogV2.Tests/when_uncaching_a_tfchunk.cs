using System;
using EventStore.Core.TransactionLogV2.Chunks.TFChunk;
using EventStore.Core.TransactionLogV2.LogRecords;
using EventStore.Core.TransactionLogV2.TestHelpers;
using EventStore.Core.TransactionLogV2.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.TransactionLogV2.Tests {
	[TestFixture]
	public class when_uncaching_a_tfchunk : SpecificationWithFilePerTestFixture {
		private TFChunk _chunk;
		private readonly Guid _corrId = Guid.NewGuid();
		private readonly Guid _eventId = Guid.NewGuid();
		private RecordWriteResult _result;
		private PrepareLogRecord _record;
		private TFChunk _uncachedChunk;

		[OneTimeSetUp]
		public override void TestFixtureSetUp() {
			base.TestFixtureSetUp();
			_record = new PrepareLogRecord(0, _corrId, _eventId, 0, 0, "test", 1, new DateTime(2000, 1, 1, 12, 0, 0),
				PrepareFlags.None, "Foo", new byte[12], new byte[15]);
			_chunk = TFChunkHelper.CreateNewChunk(Filename);
			_result = _chunk.TryAppend(_record);
			_chunk.Flush();
			_chunk.Complete();
			_uncachedChunk = TFChunk.FromCompletedFile(Filename, verifyHash: true, 
				5, //Constants.TFChunkInitialReaderCountDefault,
				21);//Constants.TFChunkMaxReaderCountDefault);
			_uncachedChunk.CacheInMemory();
			_uncachedChunk.UnCacheFromMemory();
		}

		[OneTimeTearDown]
		public override void TestFixtureTearDown() {
			_chunk.Dispose();
			_uncachedChunk.Dispose();
			base.TestFixtureTearDown();
		}

		[Test]
		public void the_write_result_is_correct() {
			Assert.IsTrue(_result.Success);
			Assert.AreEqual(0, _result.OldPosition);
			Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), _result.NewPosition);
		}

		[Test]
		public void the_chunk_is_not_cached() {
			Assert.IsFalse(_uncachedChunk.IsCached);
		}

		[Test]
		public void the_record_was_written() {
			Assert.IsTrue(_result.Success);
		}

		[Test]
		public void the_correct_position_is_returned() {
			Assert.AreEqual(0, _result.OldPosition);
		}

		[Test]
		public void the_record_can_be_read() {
			var res = _uncachedChunk.TryReadAt(0);
			Assert.IsTrue(res.Success);
			Assert.AreEqual(_record, res.LogRecord);
			Assert.AreEqual(_result.OldPosition, res.LogRecord.LogPosition);
			//Assert.AreEqual(_result.NewPosition, res.NewPosition);
		}
	}
}
