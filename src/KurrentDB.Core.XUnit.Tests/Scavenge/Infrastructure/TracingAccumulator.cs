// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog.Scavenging.Data;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

namespace KurrentDB.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingAccumulator<TStreamId> : IAccumulator<TStreamId> {
	private readonly IAccumulator<TStreamId> _wrapped;
	private readonly Tracer _tracer;

	public TracingAccumulator(IAccumulator<TStreamId> wrapped, Tracer tracer) {
		_wrapped = wrapped;
		_tracer = tracer;
	}

	public async ValueTask Accumulate(
		ScavengePoint prevScavengePoint,
		ScavengePoint scavengePoint,
		IScavengeStateForAccumulator<TStreamId> state,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Accumulating from {prevScavengePoint?.GetName() ?? "start"} to {scavengePoint.GetName()}");
		try {
			await _wrapped.Accumulate(prevScavengePoint, scavengePoint, state, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception accumulating");
			throw;
		}
	}

	public async ValueTask Accumulate(
		ScavengeCheckpoint.Accumulating checkpoint,
		IScavengeStateForAccumulator<TStreamId> state,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Accumulating from checkpoint: {checkpoint}");
		try {
			await _wrapped.Accumulate(checkpoint, state, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception accumulating");
			throw;
		}
	}
}
