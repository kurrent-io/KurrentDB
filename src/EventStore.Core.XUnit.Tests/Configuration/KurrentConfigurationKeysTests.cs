// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration;

public class KurrentConfigurationKeysTests {
	[Theory]
	[InlineData("KURRENTDB_StreamInfoCacheCapacity", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("KURRENTDB_STREAM_INFO_CACHE_CAPACITY", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("KURRENTDB__STREAM_INFO_CACHE_CAPACITY", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("KURRENTDB__CLUSTER__STREAM_INFO_CACHE_CAPACITY", "KurrentDB:Cluster:StreamInfoCacheCapacity")]
	[InlineData("KURRENTDB__KEY_WITH_UNDERSCORES__ANOTHER_ONE", "KurrentDB:KeyWithUnderscores:AnotherOne")]
	[InlineData("StreamInfoCacheCapacity", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("stream-info-cache-capacity", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("KurrentDB:Cluster:StreamInfoCacheCapacity", "KurrentDB:Cluster:StreamInfoCacheCapacity")]
	[InlineData("GossipSeed", "KurrentDB:GossipSeed")]
	[InlineData("KURRENTDB_GOSSIP_SEED", "KurrentDB:GossipSeed")]
	[InlineData("KURRENTDB_GOSSIPSEED", "KurrentDB:GossipSeed")]
	public void NormalizesKurrentKeys(string key, string normalizedKey) {
		KurrentConfigurationKeys.Normalize(key).Should().Be(normalizedKey);
	}

	[Theory]
	[InlineData("EVENTSTORE_StreamInfoCacheCapacity", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("EVENTSTORE_STREAM_INFO_CACHE_CAPACITY", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("EVENTSTORE__STREAM_INFO_CACHE_CAPACITY", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("EVENTSTORE__CLUSTER__STREAM_INFO_CACHE_CAPACITY", "KurrentDB:Cluster:StreamInfoCacheCapacity")]
	[InlineData("EVENTSTORE__KEY_WITH_UNDERSCORES__ANOTHER_ONE", "KurrentDB:KeyWithUnderscores:AnotherOne")]
	[InlineData("StreamInfoCacheCapacity", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("stream-info-cache-capacity", "KurrentDB:StreamInfoCacheCapacity")]
	[InlineData("EventStore:Cluster:StreamInfoCacheCapacity", "KurrentDB:Cluster:StreamInfoCacheCapacity")]
	[InlineData("GossipSeed", "KurrentDB:GossipSeed")]
	[InlineData("EVENTSTORE_GOSSIP_SEED", "KurrentDB:GossipSeed")]
	[InlineData("EVENTSTORE_GOSSIPSEED", "KurrentDB:GossipSeed")]
	public void TransformsEventStoreEnvVars(string key, string normalizedKey) {
		KurrentConfigurationKeys.Normalize(
			KurrentConfigurationKeys.LegacyEventStorePrefix, KurrentConfigurationKeys.Prefix, key).Should().Be(normalizedKey);
	}
}
