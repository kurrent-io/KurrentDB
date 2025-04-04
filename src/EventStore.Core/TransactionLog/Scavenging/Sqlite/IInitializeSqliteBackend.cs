// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public interface IInitializeSqliteBackend {
	void Initialize(SqliteBackend sqlite);
}
