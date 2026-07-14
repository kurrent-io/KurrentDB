// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine;

using LogEntries;

partial class ClusterState {
	public void Update(AddOrUpdateDatabaseNode command, in CommandInfo info)
		=> Update<AddOrUpdateDatabaseNodeStmt>(new(command), info);

	public bool Update(RemoveDatabaseNode command, in CommandInfo info)
		=> Update<RemoveDatabaseNodeStmt, bool>(new(command), info);

	public bool Update(AppointLeader command, in CommandInfo info) {
		var result = true;
		try {
			Update<AppointLeaderNodeStmt>(new(command), info);
		} catch (StaleEpochException) {
			result = false;
		}

		return result;
	}
}

[StructLayout(LayoutKind.Auto)]
file readonly struct AddOrUpdateDatabaseNodeStmt(AddOrUpdateDatabaseNode command) :
	IPreparedStatement<(string DatabaseId, ReadOnlyMemory<byte> Address, int Role, string Version)>,
	IConsumer<DuckDBAdvancedConnection> {
	public static ReadOnlySpan<byte> CommandText => """
	                                                INSERT INTO node (database_id, address, role)
	                                                VALUES ($1, $2, $3, $4)
	                                                ON CONFLICT (database_id, address) DO UPDATE
	                                                SET role=$3, version=$4
	                                                WHERE node.database_id=$1 AND node.address=$2;
	                                                """u8;

	public static StatementBindingResult Bind(
		in (string DatabaseId, ReadOnlyMemory<byte> Address, int Role, string Version) args,
		PreparedStatement source) => new(source) {
		args.DatabaseId,
		args.Address.Span,
		args.Role,
		args.Version,
	};

	public void Invoke(DuckDBAdvancedConnection connection)
		=> connection.ExecuteNonQuery<(string, ReadOnlyMemory<byte>, int, string), AddOrUpdateDatabaseNodeStmt>(
			new(command.DatabaseId, command.Address.Memory, command.Role, command.Version));
}

[StructLayout(LayoutKind.Auto)]
file readonly struct RemoveDatabaseNodeStmt(RemoveDatabaseNode command) :
	IPreparedStatement<(string DatabaseId, ReadOnlyMemory<byte> Address)>,
	ISupplier<DuckDBAdvancedConnection, bool> {
	public static ReadOnlySpan<byte> CommandText => "DELETE FROM node WHERE database_id=$1 AND address=$2;"u8;

	public static StatementBindingResult Bind(in (string DatabaseId, ReadOnlyMemory<byte> Address) args, PreparedStatement source)
		=> new(source) {
			args.DatabaseId,
			args.Address.Span
		};

	public bool Invoke(DuckDBAdvancedConnection connection)
		=> connection.ExecuteNonQuery<(string, ReadOnlyMemory<byte>), RemoveDatabaseNodeStmt>(
			new(command.DatabaseId, command.Address.Memory)) > 0L;
}

[StructLayout(LayoutKind.Auto)]
file readonly struct UnsetLeaderNodeStmt : IPreparedStatement<ValueTuple<string>> {
	public static ReadOnlySpan<byte> CommandText => "UPDATE node SET is_leader=false WHERE database_id=?;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1
	};
}

[StructLayout(LayoutKind.Auto)]
file readonly struct IncrementEpochStmt : IPreparedStatement<(string Id, ulong Epoch)> {
	public static ReadOnlySpan<byte> CommandText => "UPDATE database SET epoch = epoch + 1 WHERE id = $1 AND epoch = $2;"u8;

	public static StatementBindingResult Bind(in (string Id, ulong Epoch) args, PreparedStatement source) => new(source) {
		args.Id,
		args.Epoch
	};
}

[StructLayout(LayoutKind.Auto)]
file readonly struct AppointLeaderNodeStmt(AppointLeader command)
	: IPreparedStatement<(string DatabaseId, ReadOnlyMemory<byte> Address)>,
		IConsumer<DuckDBAdvancedConnection> {
	public static ReadOnlySpan<byte> CommandText => "UPDATE node SET is_leader=true WHERE database_id=$1 AND address=$2;"u8;

	public static StatementBindingResult Bind(in (string DatabaseId, ReadOnlyMemory<byte> Address) args,
		PreparedStatement source)
		=> new(source) {
			args.DatabaseId,
			args.Address.Span,
		};

	public void Invoke(DuckDBAdvancedConnection connection) {
		connection.ExecuteNonQuery<ValueTuple<string>, UnsetLeaderNodeStmt>(new(command.DatabaseId));
		if (connection.ExecuteNonQuery<(string, ulong), IncrementEpochStmt>(
			    new(command.DatabaseId, command.Epoch)) is 0L)
			throw new StaleEpochException();

		connection.ExecuteNonQuery<(string, ReadOnlyMemory<byte>), AppointLeaderNodeStmt>(
			new(command.DatabaseId, command.Address.Memory));
	}
}

file sealed class StaleEpochException() : InvalidOperationException("Epoch is old.");
