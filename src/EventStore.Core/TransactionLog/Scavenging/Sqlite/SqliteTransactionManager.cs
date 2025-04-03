// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteTransactionManager : TransactionManager<SqliteTransaction> {
	public SqliteTransactionManager(ITransactionFactory<SqliteTransaction> factory,
		IScavengeMap<Unit, ScavengeCheckpoint> storage) : base(factory, storage) {
	}
}
