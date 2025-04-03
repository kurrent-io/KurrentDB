// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.InMemory;

public class InMemoryTransactionFactory : ITransactionFactory<int> {
	int _transactionNumber;

	public InMemoryTransactionFactory() {
	}

	public int Begin() {
		return _transactionNumber++;
	}

	public void Commit(int transasction) {
	}

	public void Rollback(int transaction) {
		throw new NotImplementedException();
	}
}
