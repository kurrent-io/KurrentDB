// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Scavenging.InMemory;

public class InMemoryTransactionManager : TransactionManager<int> {
	public InMemoryTransactionManager(InMemoryScavengeMap<Unit, ScavengeCheckpoint> storage)
		: base(new InMemoryTransactionFactory(), storage) {
	}
}
