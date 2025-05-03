using EventStore.Core.Services.Monitoring;
using EventStore.Core.Tests.Helpers;

namespace EventStore.Core.Tests; 

public static class ClusterVNodeOptionsExtensions {
	public static ClusterVNodeOptions ReduceMemoryUsageForTests(this ClusterVNodeOptions options) {
		return options with {
			Cluster = options.Cluster with {
				StreamInfoCacheCapacity = 10_000
			},
			Database = options.Database with {
				ChunkSize = MiniNode.ChunkSize,
				ChunksCacheSize = MiniNode.CachedChunkSize,
				ChunkCachePooling = false,
				StreamExistenceFilterSize = 10_000,
				ScavengeBackendCacheSize = 64 * 1024,
			}
		};
	}
}
