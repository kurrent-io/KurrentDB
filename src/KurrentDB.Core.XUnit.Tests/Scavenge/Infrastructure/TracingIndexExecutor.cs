// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Index;
using KurrentDB.Core.TransactionLog.Scavenging.Data;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

namespace KurrentDB.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingIndexExecutor<TStreamId> : IIndexExecutor<TStreamId> {
	private readonly IIndexExecutor<TStreamId> _wrapped;
	private readonly Tracer _tracer;

	public TracingIndexExecutor(IIndexExecutor<TStreamId> wrapped, Tracer tracer) {
		_wrapped = wrapped;
		_tracer = tracer;
	}

	public async ValueTask Execute(
		ScavengePoint scavengePoint,
		IScavengeStateForIndexExecutor<TStreamId> state,
		IIndexScavengerLog scavengerLogger,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Executing index for {scavengePoint.GetName()}");
		try {
			await _wrapped.Execute(scavengePoint, state, scavengerLogger, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception executing index");
			throw;
		}
	}

	public async ValueTask Execute(
		ScavengeCheckpoint.ExecutingIndex checkpoint,
		IScavengeStateForIndexExecutor<TStreamId> state,
		IIndexScavengerLog scavengerLogger,
		CancellationToken cancellationToken) {

		_tracer.TraceIn($"Executing index from checkpoint: {checkpoint}");
		try {
			await _wrapped.Execute(checkpoint, state, scavengerLogger, cancellationToken);
			_tracer.TraceOut("Done");
		} catch {
			_tracer.TraceOut("Exception executing index");
			throw;
		}
	}
}
