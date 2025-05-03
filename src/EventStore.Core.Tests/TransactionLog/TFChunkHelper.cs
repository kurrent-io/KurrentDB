using EventStore.Core.Settings;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.Util;

namespace EventStore.Core.Tests.TransactionLog {
	public static class TFChunkHelper {
		public static TFChunkDbConfig CreateDbConfig(
			string pathName,
			long writerCheckpointPosition) {
			return CreateDbConfigEx(pathName, writerCheckpointPosition,0,-1,-1,-1,10000,-1);
		}
		public static TFChunkDbConfig CreateSizedDbConfig(
			string pathName,
			long writerCheckpointPosition,
			int chunkSize) {
			return CreateDbConfigEx(pathName, writerCheckpointPosition,0,-1,-1,-1,chunkSize,-1);
		}
		public static TFChunkDbConfig CreateDbConfigEx(
			string pathName, 
			long writerCheckpointPosition,
			long chaserCheckpointPosition,// Default 0
			long epochCheckpointPosition ,// Default -1
			long proposalCheckpointPosition ,// Default -1
			long truncateCheckpoint ,// Default -1
			int chunkSize ,// Default 10000
			long maxTruncation // Default -1
			) {
			return new TFChunkDbConfig(pathName,
				new VersionedPatternFileNamingStrategy(pathName, "chunk-"),
				chunkSize,
				0,
				new InMemoryCheckpoint(writerCheckpointPosition),
				new InMemoryCheckpoint(chaserCheckpointPosition),
				new InMemoryCheckpoint(epochCheckpointPosition),
				new InMemoryCheckpoint(proposalCheckpointPosition),
				new InMemoryCheckpoint(truncateCheckpoint),
				new InMemoryCheckpoint(-1), 
				new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1),
				Constants.TFChunkInitialReaderCountDefault, 
				Constants.TFChunkMaxReaderCountDefault,
				maxTruncation: maxTruncation);
		}

		public static TFChunkDbConfig CreateDbConfig(
			string pathName, 
			ICheckpoint writerCheckpoint,
			ICheckpoint chaserCheckpoint, 
			int chunkSize = 10000, 
			ICheckpoint replicationCheckpoint = null) {
			if (replicationCheckpoint == null) replicationCheckpoint = new InMemoryCheckpoint(-1);
			return new TFChunkDbConfig(
				pathName,
				new VersionedPatternFileNamingStrategy(pathName, "chunk-"),
				chunkSize,
				0,
				writerCheckpoint,
				chaserCheckpoint,
				new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1),
				replicationCheckpoint, 
				new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1),
				Constants.TFChunkInitialReaderCountDefault, 
				Constants.TFChunkMaxReaderCountDefault);
		}

		public static TFChunk CreateNewChunk(string fileName, int chunkSize = 4096, bool isScavenged = false) {
			return TFChunk.CreateNew(
				new UnmanagedChunkCacheManager(),
				fileName, chunkSize, 0, 0,
				isScavenged: isScavenged, inMem: false, unbuffered: false,
				writethrough: false, initialReaderCount: Constants.TFChunkInitialReaderCountDefault, maxReaderCount: Constants.TFChunkMaxReaderCountDefault, reduceFileCachePressure: false, tracker: new TFChunkTracker.NoOp());
		}
	}
}
