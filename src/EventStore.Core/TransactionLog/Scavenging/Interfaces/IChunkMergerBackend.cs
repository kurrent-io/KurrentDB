// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IChunkMergerBackend {
	ValueTask MergeChunks(
		ITFChunkScavengerLog scavengerLogger,
		Throttle throttle,
		CancellationToken cancellationToken);
}
