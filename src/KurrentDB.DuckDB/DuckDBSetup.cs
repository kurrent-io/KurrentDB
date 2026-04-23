// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using Kurrent.Quack;

namespace KurrentDB.DuckDB;

public interface IDuckDBSetup {
	void Execute(DuckDBAdvancedConnection connection);
	bool OneTimeOnly { get; }
}

public abstract class DuckDBOneTimeSetup : IDuckDBSetup {
	private bool _created;

	public void Execute(DuckDBAdvancedConnection connection) {
		if (!Interlocked.FalseToTrue(ref _created)) {
			return;
		}
		ExecuteCore(connection);
	}

	public bool OneTimeOnly => true;

	protected abstract void ExecuteCore(DuckDBAdvancedConnection connection);
}
