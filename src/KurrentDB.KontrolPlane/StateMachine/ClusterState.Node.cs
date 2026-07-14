// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
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
	IPreparedStatement<(string DatabaseId, ReadOnlyMemory<byte> Address, int Role, string Version, ReadOnlyMemory<byte> ClientApiAddr, ReadOnlyMemory<byte> ReplicationAddr, Guid InstanceId)>,
	IConsumer<DuckDBAdvancedConnection> {
	public static ReadOnlySpan<byte> CommandText => """
	                                                INSERT INTO node (database_id, address, role, version, client_api_addr, replication_addr, instance_id)
	                                                VALUES ($1, $2, $3, $4, $5, $6, $7)
	                                                ON CONFLICT (database_id, address) DO UPDATE
	                                                SET role=$3, version=$4, client_api_addr=$5, replication_addr=$6, instance_id=$7
	                                                WHERE node.database_id=$1 AND node.address=$2;
	                                                """u8;

	public static StatementBindingResult Bind(
		in (string DatabaseId, ReadOnlyMemory<byte> Address, int Role, string Version, ReadOnlyMemory<byte> ClientApiAddr,
			ReadOnlyMemory<byte> ReplicationAddr, Guid InstanceId) args,
		PreparedStatement source) => new(source) {
		args.DatabaseId,
		args.Address.Span,
		args.Role,
		args.Version,
		args.ClientApiAddr.Span,
		args.ReplicationAddr.Span,
		Unsafe.BitCast<Guid, UInt128>(args.InstanceId),
	};

	public void Invoke(DuckDBAdvancedConnection connection)
		=> connection
			.ExecuteNonQuery<(string, ReadOnlyMemory<byte>, int, string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Guid),
				AddOrUpdateDatabaseNodeStmt>(
				new(command.DatabaseId, command.Address.Memory, command.Role, command.Version,
					command.ClientApiAddress.Memory,
					command.ReplicationProtocolAddress.Memory,
					new Guid(command.InstanceId.Span)));
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
