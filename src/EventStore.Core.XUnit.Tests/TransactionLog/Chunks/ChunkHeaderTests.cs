// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Plugins.Transforms;
using Xunit;

namespace EventStore.Core.XUnit.Tests.TransactionLog.Chunks;

public class ChunkHeaderTests {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_round_trip(bool isScavenged) {
		var source = new ChunkHeader(
			version: (byte)Random.Shared.Next(4, 8),
			minCompatibleVersion: (byte)Random.Shared.Next(4),
			chunkSize: Random.Shared.Next(500, 600),
			chunkStartNumber: Random.Shared.Next(500, 600),
			chunkEndNumber: Random.Shared.Next(700, 800),
			isScavenged: isScavenged,
			chunkId: Guid.NewGuid(),
			transformType: TransformType.Identity);

		var destination = new ChunkHeader(source.AsByteArray());

		Assert.Equal(source.Version, destination.Version);
		Assert.Equal(source.MinCompatibleVersion, destination.MinCompatibleVersion);
		Assert.Equal(source.ChunkSize, destination.ChunkSize);
		Assert.Equal(source.ChunkStartNumber, destination.ChunkStartNumber);
		Assert.Equal(source.ChunkEndNumber, destination.ChunkEndNumber);
		Assert.Equal(source.IsScavenged, destination.IsScavenged);
		Assert.Equal(source.ChunkId, destination.ChunkId);
		Assert.Equal(source.TransformType, destination.TransformType);
	}
}
