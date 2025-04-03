// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging;

// This makes sure we dont accidentally start trying to nest transactions or begin them concurrently
// and facilitates committing the open transaction with a checkpoint
public class TransactionManager<TTransaction> : ITransactionManager {
	private readonly ITransactionFactory<TTransaction> _factory;
	private readonly IScavengeMap<Unit, ScavengeCheckpoint> _storage;
	private Action _onRollback;
	private bool _began;
	private TTransaction _transaction;

	public TransactionManager(
		ITransactionFactory<TTransaction> factory,
		IScavengeMap<Unit, ScavengeCheckpoint> storage) {

		_factory = factory;
		_storage = storage;
	}

	public void RegisterOnRollback(Action onRollback) {
		if (_onRollback != null)
			throw new InvalidOperationException();

		_onRollback = onRollback;
	}

	public void UnregisterOnRollback() {
		_onRollback = null;
	}

	public void Begin() {
		if (_began)
			throw new InvalidOperationException("Cannot begin a transaction that has already begun.");

		_transaction = _factory.Begin();
		_began = true;
	}

	public void Rollback() {
		if (!_began)
			throw new InvalidOperationException("Cannot rollback a transaction that has not begun.");

		_factory.Rollback(_transaction);
		_onRollback?.Invoke();
		_began = false;
	}

	public void Commit(ScavengeCheckpoint checkpoint) {
		if (!_began)
			throw new InvalidOperationException("Cannot commit a transaction that has not begun.");

		_storage[Unit.Instance] = checkpoint;

		_factory.Commit(_transaction);
		_began = false;
	}
}
