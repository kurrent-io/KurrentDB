// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

internal static class QueryHelpers {
	extension(DuckDBAdvancedConnection connection) {
		public QueryResult<string, AllDatabasesQuery> GetDatabases()
			=> connection.ExecuteQuery<string, AllDatabasesQuery>();

		public QueryResult<(string Id, ulong Epoch), AllDatabasesWithEpochQuery> GetDatabasesWithEpoch()
			=> connection.ExecuteQuery<(string, ulong), AllDatabasesWithEpochQuery>();

		public QueryResult<ValueTuple<string>, (EndPoint Address, DatabaseNodeRole Role, bool IsLeader, string Version, EndPoint? ClientApi, EndPoint Replication), AllNodesQuery> GetDatabaseNodes(
			string databaseId)
			=> connection.ExecuteQuery<ValueTuple<string>, (EndPoint, DatabaseNodeRole, bool, string, EndPoint?, EndPoint), AllNodesQuery>(new(databaseId));

		public QueryResult<ValueTuple<string>, (string Description, ulong Epoch), DatabaseQuery> GetDatabase(string databaseId)
			=> connection.ExecuteQuery<ValueTuple<string>, (string, ulong), DatabaseQuery>(new(databaseId));
	}
}
