// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingCalculator<TStreamId> : ICalculator<TStreamId> {
	private readonly ICalculator<TStreamId> _wrapped;
	private readonly Tracer _tracer;

	public TracingCalculator(ICalculator<TStreamId> wrapped, Tracer tracer) {
		_wrapped = wrapped;
		_tracer = tracer;
	}

	public async ValueTask Calculate(
		ScavengePoint scavengePoint,
		IScavengeStateForCalculator<TStreamId> source,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Calculating {scavengePoint.GetName()}");
		try {
			await _wrapped.Calculate(scavengePoint, source, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception calculating");
			throw;
		}
	}

	public async ValueTask Calculate(
		ScavengeCheckpoint.Calculating<TStreamId> checkpoint,
		IScavengeStateForCalculator<TStreamId> source,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Calculating from checkpoint: {checkpoint}");
		try {
			await _wrapped.Calculate(checkpoint, source, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception calculating");
			throw;
		}
	}
}
