using System;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_reading_logical_bytes_bulk_from_a_chunk<TLogFormat, TStreamId> : SpecificationWithDirectory {
		public when_reading_logical_bytes_bulk_from_a_chunk() {
		}

		[Test]
		public void the_file_will_not_be_deleted_until_reader_released() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 2000);
			using (var reader = chunk.AcquireReader()) {
				chunk.MarkForDeletion();
				var buffer = new byte[1024];
				var result = reader.ReadNextDataBytes(1024, buffer);
				Assert.IsFalse(result.IsEOF);
				Assert.AreEqual(0, result.BytesRead); // no data yet
			}

			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void a_read_on_new_file_can_be_performed_but_returns_nothing() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 2000);
			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextDataBytes(1024, buffer);
				Assert.IsFalse(result.IsEOF);
				Assert.AreEqual(0, result.BytesRead);
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void a_read_past_end_of_completed_chunk_does_not_include_footer() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 300);
			chunk.Complete(); // chunk has 0 bytes of actual data
			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextDataBytes(1024, buffer);
				Assert.IsTrue(result.IsEOF);
				Assert.AreEqual(0, result.BytesRead);
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}


		[Test]
		public void a_read_on_scavenged_chunk_does_not_include_map() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("afile"), 200, isScavenged: true);
			chunk.CompleteScavenge(new[] { new PosMap(0, 0), new PosMap(1, 1) });
			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextDataBytes(1024, buffer);
				Assert.IsTrue(result.IsEOF);
				Assert.AreEqual(0, result.BytesRead); //header 128 + footer 128 + map 16
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void if_asked_for_more_than_buffer_size_will_only_read_buffer_size() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 3000);
			var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
			var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
			var rec = LogRecord.Prepare(recordFactory, 0, Guid.NewGuid(),
				Guid.NewGuid(), 0, 0, streamId, -1, PrepareFlags.None, eventTypeId,
				new byte[2000], null);
			Assert.IsTrue(chunk.TryAppend(rec).Success, "Record was not appended");

			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextDataBytes(3000, buffer);
				Assert.IsFalse(result.IsEOF);
				Assert.AreEqual(1024, result.BytesRead);
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void a_read_past_eof_doesnt_return_eof_if_chunk_is_not_yet_completed() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 300);
			var rec = LogRecord.Commit(0, Guid.NewGuid(), 0, 0);
			Assert.IsTrue(chunk.TryAppend(rec).Success, "Record was not appended");
			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextDataBytes(1024, buffer);
				Assert.IsFalse(result.IsEOF, "EOF was returned.");
				//does not include header and footer space
				Assert.AreEqual(rec.GetSizeWithLengthPrefixAndSuffix(), result.BytesRead,
					"Read wrong number of bytes.");
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void a_read_past_eof_returns_eof_if_chunk_is_completed() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 300);

			var rec = LogRecord.Commit(0, Guid.NewGuid(), 0, 0);
			Assert.IsTrue(chunk.TryAppend(rec).Success, "Record was not appended");
			chunk.Complete();

			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextDataBytes(1024, buffer);
				Assert.IsTrue(result.IsEOF, "EOF was not returned.");
				//does not include header and footer space
				Assert.AreEqual(rec.GetSizeWithLengthPrefixAndSuffix(), result.BytesRead,
					"Read wrong number of bytes.");
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}
	}
}
