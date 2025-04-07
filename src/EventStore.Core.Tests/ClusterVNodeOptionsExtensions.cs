// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
				StreamExistenceFilterSize = 10_000,
				ScavengeBackendCacheSize = 64 * 1024,
			}
		};
	}
}
