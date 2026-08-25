// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.Raft.StateMachine.Queries;

internal static class QueryHelpers {
	extension(DuckDBAdvancedConnection connection) {
		public QueryResult<string, AllDatabasesQuery> GetDatabases()
			=> connection.ExecuteQuery<string, AllDatabasesQuery>();

		public QueryResult<(string Id, ulong Epoch), AllDatabasesWithEpochQuery> GetDatabasesWithEpoch()
			=> connection.ExecuteQuery<(string, ulong), AllDatabasesWithEpochQuery>();

		public QueryResult<ValueTuple<string>, PersistentDatabaseNode, AllNodesQuery> GetDatabaseNodes(
			string databaseId)
			=> connection.ExecuteQuery<ValueTuple<string>, PersistentDatabaseNode, AllNodesQuery>(new(databaseId));

		public QueryResult<ValueTuple<string>, (string Description, ulong Epoch), DatabaseQuery> GetDatabase(string databaseId)
			=> connection.ExecuteQuery<ValueTuple<string>, (string, ulong), DatabaseQuery>(new(databaseId));
	}
}
