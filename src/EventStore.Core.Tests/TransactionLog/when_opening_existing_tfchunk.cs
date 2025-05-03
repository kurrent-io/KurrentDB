using System;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
	[TestFixture]
	public class when_opening_existing_tfchunk : SpecificationWithFilePerTestFixture {
		private TFChunk _chunk;
		private TFChunk _testChunk;

		[OneTimeSetUp]
		public override void TestFixtureSetUp() {
			base.TestFixtureSetUp();
			_chunk = TFChunkHelper.CreateNewChunk(Filename);
			_chunk.Complete();
			_testChunk = TFChunk.FromCompletedFile(
				new UnmanagedChunkCacheManager(),
				Filename, true, false,
				Constants.TFChunkInitialReaderCountDefault,
				Constants.TFChunkMaxReaderCountDefault,
				reduceFileCachePressure: false, tracker: new TFChunkTracker.NoOp());
		}

		[TearDown]
		public override void TestFixtureTearDown() {
			_chunk.Dispose();
			_testChunk.Dispose();
			base.TestFixtureTearDown();
		}

		[Test]
		public void the_chunk_is_not_cached() {
			Assert.IsFalse(_testChunk.IsCached);
		}

		[Test]
		public void the_chunk_is_readonly() {
			Assert.IsTrue(_testChunk.IsReadOnly);
		}

		[Test]
		public void append_throws_invalid_operation_exception() {
			Assert.Throws<InvalidOperationException>(() =>
				_testChunk.TryAppend(new CommitLogRecord(0, Guid.NewGuid(), 0, DateTime.UtcNow, 0)));
		}

		[Test]
		public void flush_throws_invalid_operation_exception() {
			Assert.Throws<InvalidOperationException>(() => _testChunk.Flush());
		}
	}
}
