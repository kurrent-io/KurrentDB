using System;
using System.IO;
using System.Threading.Tasks;
using EventStore.Core.Data.Redaction;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RedactionService {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class SwitchChunkFailureTests<TLogFormat, TStreamId> : SwitchChunkTests<TLogFormat, TStreamId> {
		[Test]
		public async Task cannot_switch_invalid_chunk_filename() {
			var msg = await SwitchChunk("chunk", FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkFileNameInvalid, msg.Result);

			msg = await SwitchChunk("../chunk-000000.000000", FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkFileNameInvalid, msg.Result);

			msg = await SwitchChunk("..chunk-000000.000000", FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkFileNameInvalid, msg.Result);

			msg = await SwitchChunk("/chunk-000000.000000", FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkFileNameInvalid, msg.Result);
		}

		[Test]
		public async Task cannot_switch_non_existent_chunk() {
			var msg = await SwitchChunk(GetChunk(3, 0), FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkFileNotFound, msg.Result);
		}

		[Test]
		public async Task cannot_switch_chunk_not_used_by_db() {
			File.Copy(GetChunk(0, 0, true), GetChunk(10, 0, true));
			var msg = await SwitchChunk(GetChunk(10, 0), FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkExcessive, msg.Result);
		}

		[Test]
		public async Task cannot_switch_inactive_chunk() {
			File.Copy(GetChunk(0, 0, true), GetChunk(0, 1, true));
			var msg = await SwitchChunk(GetChunk(0, 1), FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkInactive, msg.Result);
		}

		[Test]
		public async Task cannot_switch_incomplete_chunk() {
			var msg = await SwitchChunk(GetChunk(2, 0), FakeChunk);
			Assert.AreEqual(SwitchChunkResult.TargetChunkNotCompleted, msg.Result);
		}

		[Test]
		public async Task cannot_switch_with_invalid_chunk_filename() {
			var msg = await SwitchChunk(GetChunk(0, 0), "chunk.wrong_extension");
			Assert.AreEqual(SwitchChunkResult.NewChunkFileNameInvalid, msg.Result);

			msg = await SwitchChunk(GetChunk(0, 0), "../chunk.tmp");
			Assert.AreEqual(SwitchChunkResult.NewChunkFileNameInvalid, msg.Result);

			msg = await SwitchChunk(GetChunk(0, 0), "..chunk.tmp");
			Assert.AreEqual(SwitchChunkResult.NewChunkFileNameInvalid, msg.Result);

			msg = await SwitchChunk(GetChunk(0, 0), "/chunk.tmp");
			Assert.AreEqual(SwitchChunkResult.NewChunkFileNameInvalid, msg.Result);
		}

		[Test]
		public async Task cannot_switch_with_non_existent_chunk() {
			var msg = await SwitchChunk(GetChunk(0, 0), "no-chunk.tmp");
			Assert.AreEqual(SwitchChunkResult.NewChunkFileNotFound, msg.Result);
		}

		[Test]
		public async Task cannot_switch_with_incomplete_chunk() {
			var newChunk = Path.Combine(PathName, $"{nameof(cannot_switch_with_incomplete_chunk)}.tmp");
			File.Copy(GetChunk(0, 0, true), newChunk);

			// zero out the footer & hash to make it an incomplete chunk
			File.SetAttributes(newChunk, FileAttributes.Normal);
			await using (var fs = new FileStream(newChunk, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
				fs.Seek(- (ChunkFooter.Size + ChunkFooter.ChecksumSize), SeekOrigin.End);
				fs.Write(new byte[ChunkFooter.Size + ChunkFooter.ChecksumSize]);
			}

			var msg = await SwitchChunk(GetChunk(0, 0), Path.GetFileName(newChunk));
			Assert.AreEqual(SwitchChunkResult.NewChunkNotCompleted, msg.Result);
		}

		[Test]
		public async Task cannot_switch_with_chunk_with_invalid_header_or_footer() {
			var msg = await SwitchChunk(GetChunk(0, 0), FakeChunk);
			Assert.AreEqual(SwitchChunkResult.NewChunkHeaderOrFooterInvalid, msg.Result);
		}

		[Test]
		public async Task cannot_switch_with_chunk_having_invalid_hash() {
			var newChunk = Path.Combine(PathName, $"{nameof(cannot_switch_with_chunk_having_invalid_hash)}.tmp");
			File.Copy(GetChunk(0, 0, true), newChunk);

			// corrupt the file
			File.SetAttributes(newChunk, FileAttributes.Normal);
			await using (var fs = new FileStream(newChunk, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
				fs.Seek(123, SeekOrigin.Begin);
				fs.WriteByte(0xFF);
			}

			var msg = await SwitchChunk(GetChunk(0, 0), Path.GetFileName(newChunk));
			Assert.AreEqual(SwitchChunkResult.NewChunkHashInvalid, msg.Result);
		}

		[Test]
		public async Task cannot_switch_with_chunk_file_in_use() {
			var newChunk = Path.Combine(PathName, $"{nameof(cannot_switch_with_chunk_file_in_use)}.tmp");

			File.Copy(GetChunk(0, 0, true), newChunk);
			File.SetAttributes(newChunk, FileAttributes.Normal);
			await using (new FileStream(newChunk, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
				var msg = await SwitchChunk(GetChunk(0, 0), Path.GetFileName(newChunk));
				Assert.AreEqual(SwitchChunkResult.NewChunkOpenFailed, msg.Result);
			}
		}

		[Test]
		public async Task cannot_switch_with_chunk_having_mismatched_range() {
			var newChunk = $"{nameof(cannot_switch_with_chunk_having_mismatched_range)}-chunk0copy.tmp";
			File.Copy(GetChunk(0, 0, true), Path.Combine(PathName, newChunk));
			var msg = await SwitchChunk(GetChunk(1, 0), newChunk);
			Assert.AreEqual(SwitchChunkResult.ChunkRangeDoesNotMatch, msg.Result);

			newChunk = $"{nameof(cannot_switch_with_chunk_having_mismatched_range)}-chunk-0-2.tmp";
			var chunkHeader = new ChunkHeader(1, 1024, 0, 2, true, Guid.NewGuid());
			var chunk = TFChunk.CreateWithHeader(
				new UnmanagedChunkCacheManager(),
				Path.Combine(PathName, newChunk), chunkHeader, 1024, false, false, false, 1, 1, false,
				new TFChunkTracker.NoOp());
			chunk.Dispose();
			msg = await SwitchChunk(GetChunk(0, 0), newChunk);
			Assert.AreEqual(SwitchChunkResult.ChunkRangeDoesNotMatch, msg.Result);
		}
	}
}
