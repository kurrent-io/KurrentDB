// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingChunkMerger : IChunkMerger {
	private readonly IChunkMerger _wrapped;
	private readonly Tracer _tracer;

	public TracingChunkMerger(IChunkMerger wrapped, Tracer tracer) {
		_wrapped = wrapped;
		_tracer = tracer;
	}

	public async ValueTask MergeChunks(
		ScavengePoint scavengePoint,
		IScavengeStateForChunkMerger state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Merging chunks for {scavengePoint.GetName()}");
		try {
			await _wrapped.MergeChunks(scavengePoint, state, scavengerLogger, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception merging chunks");
			throw;
		}
	}

	public async ValueTask MergeChunks(
		ScavengeCheckpoint.MergingChunks checkpoint,
		IScavengeStateForChunkMerger state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Merging chunks from checkpoint: {checkpoint}");
		try {
			await _wrapped.MergeChunks(checkpoint, state, scavengerLogger, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception merging chunks");
			throw;
		}
	}
}
