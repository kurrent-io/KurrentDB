// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using DuckDB.NET.Data;
using Kurrent.Quack;

namespace KurrentDB.DuckDB;

public interface IDuckDBSetup {
	void Execute(DuckDBAdvancedConnection connection, bool initialSetup);
	bool OneTimeOnly { get; }
}

public abstract class DuckDBOneTimeSetup : IDuckDBSetup {
	private Atomic.Boolean _created;

	public void Execute(DuckDBAdvancedConnection connection, bool initialSetup) {
		if (!_created.FalseToTrue()) {
			return;
		}
		ExecuteCore(connection, initialSetup);
	}

	public bool OneTimeOnly => true;

	protected abstract void ExecuteCore(DuckDBAdvancedConnection connection, bool initialSetup);
}
