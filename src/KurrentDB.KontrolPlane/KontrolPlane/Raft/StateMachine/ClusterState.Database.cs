// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

using LogEntries;

partial class ClusterState {
	public void Update(AddOrUpdateDatabase command, in CommandInfo info)
		=> Update<AddOrUpdateDatabaseStmt>(new(command), info);

	public bool Update(RemoveDatabase command, in CommandInfo info)
		=> command.DatabaseId is not Database.MainDatabaseId && Update<RemoveDatabaseStmt, bool>(new(command), info);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct AddOrUpdateDatabaseStmt(AddOrUpdateDatabase command) : IPreparedStatement<(string DatabaseId, string Description)>, IConsumer<DuckDBAdvancedConnection> {
	public static ReadOnlySpan<byte> CommandText => """
	                                                INSERT INTO database (id, description)
	                                                VALUES ($1, $2)
	                                                ON CONFLICT (id) DO UPDATE
	                                                SET description = $2
	                                                WHERE database.id = $1;
	                                                """u8;

	public static StatementBindingResult Bind(in (string DatabaseId, string Description) args, PreparedStatement source)
		=> new(source) {
			args.DatabaseId,
			args.Description,
		};

	public void Invoke(DuckDBAdvancedConnection connection)
		=> connection.ExecuteNonQuery<(string, string), AddOrUpdateDatabaseStmt>(new(command.DatabaseId, command.Description));
}

[StructLayout(LayoutKind.Auto)]
file readonly struct RemoveDatabaseNodesStmt : IPreparedStatement<ValueTuple<string>> {
	public static ReadOnlySpan<byte> CommandText => "DELETE FROM node WHERE database_id = ?;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1,
	};
}

[StructLayout(LayoutKind.Auto)]
file readonly struct RemoveDatabaseStmt(RemoveDatabase command) : IPreparedStatement<ValueTuple<string>>, ISupplier<DuckDBAdvancedConnection, bool> {
	public static ReadOnlySpan<byte> CommandText => "DELETE FROM database WHERE id = ?;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1,
	};

	public bool Invoke(DuckDBAdvancedConnection connection) {
		connection.ExecuteNonQuery<ValueTuple<string>, RemoveDatabaseNodesStmt>(new(command.DatabaseId));
		return connection.ExecuteNonQuery<ValueTuple<string>, RemoveDatabaseStmt>(new(command.DatabaseId)) > 0L;
	}
}
