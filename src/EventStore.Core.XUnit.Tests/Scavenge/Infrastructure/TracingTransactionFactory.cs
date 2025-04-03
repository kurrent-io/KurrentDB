// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingTransactionFactory<TTransaction> : ITransactionFactory<TTransaction> {
	private readonly ITransactionFactory<TTransaction> _wrapped;
	private readonly Tracer _tracer;

	public TracingTransactionFactory(ITransactionFactory<TTransaction> wrapped, Tracer tracer) {
		_wrapped = wrapped;
		_tracer = tracer;
	}

	public TTransaction Begin() {
		_tracer.TraceIn("Begin");
		return _wrapped.Begin();
	}

	public void Commit(TTransaction transaction) {
		_wrapped.Commit(transaction);
		_tracer.TraceOut("Commit");
	}

	public void Rollback(TTransaction transaction) {
		_wrapped.Rollback(transaction);
		_tracer.TraceOut("Rollback");
	}
}
