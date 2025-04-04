// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class AdHocTransactionManager : ITransactionManager {
	private readonly ITransactionManager _wrapped;
	private readonly Action<Action<ScavengeCheckpoint>, ScavengeCheckpoint> _f;

	public AdHocTransactionManager(
		ITransactionManager wrapped,
		Action<Action<ScavengeCheckpoint>, ScavengeCheckpoint> f) {

		_wrapped = wrapped;
		_f = f;
	}

	public void RegisterOnRollback(Action onRollback) {
		_wrapped.RegisterOnRollback(onRollback);
	}

	public void UnregisterOnRollback() {
		_wrapped.UnregisterOnRollback();
	}

	public void Begin() {
		_wrapped.Begin();
	}

	public void Commit(ScavengeCheckpoint checkpoint) {
		_f(_wrapped.Commit, checkpoint);
	}

	public void Rollback() {
		_wrapped.Rollback();
	}
}
