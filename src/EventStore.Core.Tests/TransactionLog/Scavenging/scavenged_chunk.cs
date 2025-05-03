using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Scavenging {
	[TestFixture]
	public class scavenged_chunk : SpecificationWithFile {
		[Test]
		public void is_fully_resident_in_memory_when_cached() {
			var map = new List<PosMap>();
			var chunk = TFChunk.CreateNew(
				new UnmanagedChunkCacheManager(),
				Filename, 1024 * 1024, 0, 0, true, false, false, false,
				Constants.TFChunkInitialReaderCountDefault,
				Constants.TFChunkMaxReaderCountDefault,
				false,
				new TFChunkTracker.NoOp());
			long logPos = 0;
			for (int i = 0, n = ChunkFooter.Size / PosMap.FullSize + 1; i < n; ++i) {
				map.Add(new PosMap(logPos, (int)logPos));
				var res = chunk.TryAppend(LogRecord.Commit(logPos, Guid.NewGuid(), logPos, 0));
				Assert.IsTrue(res.Success);
				logPos = res.NewPosition;
			}

			chunk.CompleteScavenge(map);

			chunk.CacheInMemory();

			Assert.IsTrue(chunk.IsCached);

			var last = chunk.TryReadLast();
			Assert.IsTrue(last.Success);
			Assert.AreEqual(map[map.Count - 1].ActualPos, last.LogRecord.LogPosition);

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(1000);
		}
	}
}
