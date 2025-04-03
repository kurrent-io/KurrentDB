// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteTransactionFactory : IInitializeSqliteBackend, ITransactionFactory<SqliteTransaction> {
	private SqliteBackend _sqliteBackend;

	public void Initialize(SqliteBackend sqlite) {
		_sqliteBackend = sqlite;
	}

	public SqliteTransaction Begin() {
		return _sqliteBackend.BeginTransaction();
	}

	public void Rollback(SqliteTransaction transaction) {
		transaction.Rollback();
		transaction.Dispose();
		_sqliteBackend.ClearTransaction();
	}

	public void Commit(SqliteTransaction transaction) {
		transaction.Commit();
		transaction.Dispose();
		_sqliteBackend.ClearTransaction();
	}
}
