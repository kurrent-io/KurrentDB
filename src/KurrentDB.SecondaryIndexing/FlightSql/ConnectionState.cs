// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using KurrentDB.SecondaryIndexing.Query;

namespace KurrentDB.SecondaryIndexing.FlightSql;

/// <summary>
/// Maintains the connection-level state required to implement stateful FlightSQL operations.
/// </summary>
/// <param name="engine"></param>
internal sealed partial class ConnectionState(IQueryEngine engine) : Disposable {
	protected override void Dispose(bool disposing) {
		if (disposing) {
			foreach (var statement in _statements.Values) {
				statement.Dispose();
			}

			_statements.Clear();
		}
		base.Dispose(disposing);
	}
}
