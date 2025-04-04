using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
	[TestFixture]
	public class when_reading_physical_bytes_bulk_from_a_chunk : SpecificationWithDirectory {
		[Test]
		public void the_file_will_not_be_deleted_until_reader_released() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 2000);
			using (var reader = chunk.AcquireReader()) {
				chunk.MarkForDeletion();
				var buffer = new byte[1024];
				var result = reader.ReadNextRawBytes(1024, buffer);
				Assert.IsFalse(result.IsEOF);
				Assert.AreEqual(1024, result.BytesRead);
			}

			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void a_read_on_new_file_can_be_performed() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 2000);
			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextRawBytes(1024, buffer);
				Assert.IsFalse(result.IsEOF);
				Assert.AreEqual(1024, result.BytesRead);
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}
		/*
				[Test]
				public void a_read_on_scavenged_chunk_includes_map()
				{
					var chunk = TFChunk.CreateNew(GetFilePathFor("afile"), 200, 0, 0, isScavenged: true, inMem: false, unbuffered: false, writethrough: false);
					chunk.CompleteScavenge(new [] {new PosMap(0, 0), new PosMap(1,1) }, false);
					using (var reader = chunk.AcquireReader())
					{
						var buffer = new byte[1024];
						var result = reader.ReadNextRawBytes(1024, buffer);
						Assert.IsFalse(result.IsEOF);
						Assert.AreEqual(ChunkHeader.Size + ChunkHeader.Size + 2 * PosMap.FullSize, result.BytesRead);
					}
					chunk.MarkForDeletion();
					chunk.WaitForDestroy(5000);
				}

				[Test]
				public void a_read_past_end_of_completed_chunk_does_include_header_or_footer()
				{
					var chunk = TFChunk.CreateNew(GetFilePathFor("File1"), 300, 0, 0, isScavenged: false, inMem: false, unbuffered: false, writethrough: false);
					chunk.Complete();
					using (var reader = chunk.AcquireReader())
					{
						var buffer = new byte[1024];
						var result = reader.ReadNextRawBytes(1024, buffer);
						Assert.IsTrue(result.IsEOF);
						Assert.AreEqual(ChunkHeader.Size + ChunkFooter.Size, result.BytesRead); //just header + footer = 256
					}
					chunk.MarkForDeletion();
					chunk.WaitForDestroy(5000);
				}
		*/

		[Test]
		public void if_asked_for_more_than_buffer_size_will_only_read_buffer_size() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 3000);
			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[1024];
				var result = reader.ReadNextRawBytes(3000, buffer);
				Assert.IsFalse(result.IsEOF);
				Assert.AreEqual(1024, result.BytesRead);
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void a_read_past_eof_returns_eof_and_no_footer() {
			var chunk = TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 300);
			using (var reader = chunk.AcquireReader()) {
				var buffer = new byte[8092];
				var result = reader.ReadNextRawBytes(8092, buffer);
				Assert.IsTrue(result.IsEOF);
				Assert.AreEqual(4096, result.BytesRead); //does not includes header and footer space
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}
	}
}
