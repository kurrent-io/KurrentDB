// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Quack;
using KurrentDB.Core.Bus;
using KurrentDB.DuckDB;

namespace KurrentDB.Core.DuckDB;

public class KdbGetEventSetup(IPublisher publisher) : DuckDBOneTimeSetup {
	protected override void ExecuteCore(DuckDBAdvancedConnection connection)
		=> new GetDatabaseEventsFunction(publisher).Register(connection);
}
