// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

// There are two kinds of streams that we might want to remove events from
//    - original streams (i.e. streams with metadata)
//        - according to tombstone
//        - according to metadata (maxage, maxcount, tb)
//    - metadata streams
//        - according to tombstone
//        - maxcount 1
//
// In a nutshell:
// - The Accumulator passes through the log once (in total, not per scavenge)
//   accumulating state that we need and calculating some DiscardPoints.
// - When a ScavengePoint is set, the Calculator uses it to finish calculating
//   the DiscardPoints.
// - The Chunk and Index Executors can then use this information to perform the
//   actual record/indexEntry removal.
// - Merger merges the chunks
// - Cleaner removes parts of the scavenge state that are no longer needed
//   (as determined by the calculator)
public interface IScavenger : IDisposable {
	string ScavengeId { get; }
	Task<ScavengeResult> ScavengeAsync(CancellationToken cancellationToken);
}
