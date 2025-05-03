using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Optimization {
	[TestFixture]
	public class tfchunkreader_existsat_optimizer_should : SpecificationWithDirectoryPerTestFixture {
		[Test]
		public void have_a_single_instance() {
			var instance1 = TFChunkReaderExistsAtOptimizer.Instance;
			var instance2 = TFChunkReaderExistsAtOptimizer.Instance;
			Assert.AreEqual(instance1, instance2);
		}

		[Test]
		public void optimize_only_maxcached_items_at_a_time() {
			int maxCached = 3;
			List<TFChunk> chunks = new List<TFChunk>();
			TFChunkReaderExistsAtOptimizer _existsAtOptimizer = new TFChunkReaderExistsAtOptimizer(maxCached);

			for (int i = 0; i < 7; i++) {
				var chunk = CreateChunk(i, true);
				chunks.Add(chunk);
				Assert.IsFalse(_existsAtOptimizer.IsOptimized(chunk));
				_existsAtOptimizer.Optimize(chunk);
			}


			//only the last maxCached chunks should still be optimized
			int cached = maxCached;
			for (int i = 7 - 1; i >= 0; i--) {
				if (cached > 0) {
					Assert.AreEqual(true, _existsAtOptimizer.IsOptimized(chunks[i]));
					cached--;
				} else {
					Assert.AreEqual(false, _existsAtOptimizer.IsOptimized(chunks[i]));
				}
			}

			foreach (var chunk in chunks) {
				chunk.MarkForDeletion();
				chunk.WaitForDestroy(5000);
			}
		}

		[Test]
		public void optimize_only_scavenged_chunks() {
			TFChunkReaderExistsAtOptimizer _existsAtOptimizer = new TFChunkReaderExistsAtOptimizer(3);
			var chunk = CreateChunk(0, false);
			_existsAtOptimizer.Optimize(chunk);
			Assert.AreEqual(false, _existsAtOptimizer.IsOptimized(chunk));

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}

		[Test]
		public void posmap_items_should_exist_in_chunk() {
			TFChunkReaderExistsAtOptimizer _existsAtOptimizer = new TFChunkReaderExistsAtOptimizer(3);
			List<PosMap> posmap;
			var chunk = CreateChunk(0, true, out posmap);

			//before optimization
			Assert.AreEqual(false, _existsAtOptimizer.IsOptimized(chunk));
			foreach (var p in posmap) {
				Assert.AreEqual(true, chunk.ExistsAt(p.LogPos));
			}

			//after optimization
			_existsAtOptimizer.Optimize(chunk);
			Assert.AreEqual(true, _existsAtOptimizer.IsOptimized(chunk));
			foreach (var p in posmap) {
				Assert.AreEqual(true, chunk.ExistsAt(p.LogPos));
			}

			chunk.MarkForDeletion();
			chunk.WaitForDestroy(5000);
		}

		private TFChunk CreateChunk(int chunkNumber, bool scavenged) {
			List<PosMap> posmap;
			return CreateChunk(chunkNumber, scavenged, out posmap);
		}

		private TFChunk CreateChunk(int chunkNumber, bool scavenged, out List<PosMap> posmap) {
			var map = new List<PosMap>();
			var chunk = TFChunk.CreateNew(
				new UnmanagedChunkCacheManager(),
				GetFilePathFor("chunk-" + chunkNumber + "-" + Guid.NewGuid()), 1024 * 1024,
				chunkNumber, chunkNumber, scavenged, false, false, false,
				Constants.TFChunkInitialReaderCountDefault,
				Constants.TFChunkMaxReaderCountDefault,
				false,
				new TFChunkTracker.NoOp());
			long offset = chunkNumber * 1024 * 1024;
			long logPos = 0 + offset;
			for (int i = 0, n = ChunkFooter.Size / PosMap.FullSize + 1; i < n; ++i) {
				if (scavenged)
					map.Add(new PosMap(logPos, (int)logPos));

				var res = chunk.TryAppend(LogRecord.Commit(logPos, Guid.NewGuid(), logPos, 0));
				Assert.IsTrue(res.Success);
				logPos = res.NewPosition + offset;
			}

			if (scavenged) {
				posmap = map;
				chunk.CompleteScavenge(map);
			} else {
				posmap = null;
				chunk.Complete();
			}

			return chunk;
		}
	}
}
