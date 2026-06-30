// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

internal static class QueryHelpers {
	public static QueryResult<string, AllDatabasesQuery> GetDatabases(
		this DuckDBAdvancedConnection connection)
		=> connection.ExecuteQuery<string, AllDatabasesQuery>();

	public static QueryResult<(string Id, ulong Epoch), AllDatabasesWithEpochQuery> GetDatabasesWithEpoch(
		this DuckDBAdvancedConnection connection)
		=> connection.ExecuteQuery<(string, ulong), AllDatabasesWithEpochQuery>();

	public static QueryResult<ValueTuple<string>, (EndPoint Address, bool IsReadOnlyReplica, bool IsLeader), AllNodesQuery> GetDatabaseNodes(
		this DuckDBAdvancedConnection connection,
		string databaseId)
		=> connection.ExecuteQuery<ValueTuple<string>, (EndPoint, bool, bool), AllNodesQuery>(new(databaseId));

	public static QueryResult<ValueTuple<string>, (EndPoint Address, ulong Epoch, bool IsReadOnlyReplica), LeaderQuery> GetDatabaseLeader(
		this DuckDBAdvancedConnection connection,
		string databaseId)
		=> connection.ExecuteQuery<ValueTuple<string>, (EndPoint, ulong, bool), LeaderQuery>(new(databaseId));

	public static QueryResult<ValueTuple<string>, (string Description, ulong Epoch), DatabaseQuery> GetDatabase(
		this DuckDBAdvancedConnection connection,
		string databaseId)
		=> connection.ExecuteQuery<ValueTuple<string>, (string, ulong), DatabaseQuery>(new(databaseId));
}
