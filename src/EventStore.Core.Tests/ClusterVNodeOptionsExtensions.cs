// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
				StreamExistenceFilterSize = 10_000,
				ScavengeBackendCacheSize = 64 * 1024,
			}
		};
	}
}
