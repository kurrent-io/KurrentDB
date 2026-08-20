// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

using LogEntries;

partial class ClusterState {
	public void Update(AddOrUpdateDatabaseNode command, in CommandInfo info)
		=> Update<AddOrUpdateDatabaseNodeStmt>(new(command), info);

	public bool Update(RemoveDatabaseNode command, in CommandInfo info)
		=> Update<RemoveDatabaseNodeStmt, bool>(new(command), info);

	public bool Update(AppointLeader command, in CommandInfo info)
		=> Update<AppointLeaderNodeStmt, bool>(new(command), info);

	public bool Update(AddOrIgnoreDatabaseNode command, in CommandInfo info)
		=> Update<AddOrIgnoreDatabaseNodeStmt, bool>(new(command), info);

	public bool Update(ResignLeader command, in CommandInfo info)
		=> Update<UnsetLeaderNodeStmt, bool>(new(command), info);
}

[StructLayout(LayoutKind.Auto)]
file readonly struct AddOrIgnoreDatabaseNodeStmt(AddOrIgnoreDatabaseNode command)
	: IPreparedStatement<(string DatabaseId, ReadOnlyMemory<byte> Address, int Role, string Version, ReadOnlyMemory<byte> ClientApiAddr,
			ReadOnlyMemory<byte> ReplicationAddr, Guid InstanceId)>,
		ISupplier<DuckDBAdvancedConnection, bool> {
	public static ReadOnlySpan<byte> CommandText => """
	                                                INSERT OR IGNORE INTO node (database_id, address, role, version, client_api_addr, replication_addr, instance_id)
	                                                VALUES ($1, $2, $3, $4, $5, $6, $7)
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

	public bool Invoke(DuckDBAdvancedConnection connection)
		=> connection
			.ExecuteNonQuery<(string, ReadOnlyMemory<byte>, int, string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Guid),
				AddOrIgnoreDatabaseNodeStmt>(
				new(command.DatabaseId,
					command.Address.Memory,
					command.Role,
					command.Version,
					command.ClientApiAddress.Memory,
					command.ReplicationProtocolAddress.Memory,
					new Guid(command.InstanceId.Span))) > 0L;
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
				new(command.DatabaseId,
					command.Address.Memory,
					command.Role,
					command.Version,
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
file readonly struct UnsetLeaderNodeStmt(ResignLeader command) : IPreparedStatement<ValueTuple<string>>, ISupplier<DuckDBAdvancedConnection, bool> {
	public static ReadOnlySpan<byte> CommandText => "UPDATE node SET is_leader=false WHERE database_id=?;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1
	};

	public bool Invoke(DuckDBAdvancedConnection connection)
		=> (command.HasEpoch
			? connection.ExecuteNonQuery<(string, ulong), UnsetLeaderNodeConditionallyStmt>(new(command.DatabaseId, command.Epoch))
			: connection.ExecuteNonQuery<ValueTuple<string>, UnsetLeaderNodeStmt>(new(command.DatabaseId))) is not 0L;
}

[StructLayout(LayoutKind.Auto)]
file readonly struct UnsetLeaderNodeConditionallyStmt : IPreparedStatement<(string Id, ulong Epoch)> {
	public static ReadOnlySpan<byte> CommandText => """
	                                                UPDATE node
	                                                SET is_leader=false
	                                                FROM database d
	                                                WHERE database_id=$1 AND d.id=$1 AND d.epoch=$2;
	                                                """u8;

	public static StatementBindingResult Bind(in (string Id, ulong Epoch) args, PreparedStatement source) => new(source) {
		args.Id,
		args.Epoch
	};
}

[StructLayout(LayoutKind.Auto)]
file readonly struct AppointLeaderNodeStmt(AppointLeader command)
	: IPreparedStatement<(string DatabaseId, ReadOnlyMemory<byte> Address)>,
		ISupplier<DuckDBAdvancedConnection, bool> {
	public static ReadOnlySpan<byte> CommandText => "UPDATE node SET is_leader=true WHERE database_id=$1 AND address=$2;"u8;

	public static StatementBindingResult Bind(in (string DatabaseId, ReadOnlyMemory<byte> Address) args,
		PreparedStatement source)
		=> new(source) {
			args.DatabaseId,
			args.Address.Span,
		};

	public bool Invoke(DuckDBAdvancedConnection connection)
		=> connection.ExecuteNonQuery<(string, ulong), UnsetLeaderNodeConditionallyStmt>(new(command.DatabaseId, command.Epoch)) is not 0L
		   && connection.ExecuteNonQuery<(string, ReadOnlyMemory<byte>), AppointLeaderNodeStmt>(new(command.DatabaseId,
			   command.Address.Memory)) is not 0L;
}
