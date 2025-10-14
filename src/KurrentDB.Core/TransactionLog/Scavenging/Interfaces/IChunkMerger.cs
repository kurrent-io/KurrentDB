// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Scavenging.Data;

namespace KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

public interface IChunkMerger {
	ValueTask MergeChunks(
		ScavengePoint scavengePoint,
		IScavengeStateForChunkMerger state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken);

	ValueTask MergeChunks(
		ScavengeCheckpoint.MergingChunks checkpoint,
		IScavengeStateForChunkMerger state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken);
}
