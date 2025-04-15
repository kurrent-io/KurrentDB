// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using KurrentDB.Core.TransactionLog.Scavenging.Data;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

namespace KurrentDB.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingCleaner : ICleaner {
	private readonly ICleaner _wrapped;
	private readonly Tracer _tracer;

	public TracingCleaner(ICleaner wrapped, Tracer tracer) {
		_wrapped = wrapped;
		_tracer = tracer;
	}

	public void Clean(
		ScavengePoint scavengePoint,
		IScavengeStateForCleaner state,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Cleaning for {scavengePoint.GetName()}");
		try {
			_wrapped.Clean(scavengePoint, state, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception cleaning");
			throw;
		}
	}

	public void Clean(
		ScavengeCheckpoint.Cleaning checkpoint,
		IScavengeStateForCleaner state,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Cleaning from checkpoint {checkpoint}");
		try {
			_wrapped.Clean(checkpoint, state, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception cleaning");
			throw;
		}
	}
}
