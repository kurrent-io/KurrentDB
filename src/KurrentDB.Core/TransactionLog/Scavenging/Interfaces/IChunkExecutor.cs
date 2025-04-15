// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Scavenging.Data;

namespace KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

// The chunk executor performs the actual removal of the log records
public interface IChunkExecutor<TStreamId> {
	ValueTask Execute(
		ScavengePoint scavengePoint,
		IScavengeStateForChunkExecutor<TStreamId> state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken);

	ValueTask Execute(
		ScavengeCheckpoint.ExecutingChunks checkpoint,
		IScavengeStateForChunkExecutor<TStreamId> state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken);
}
