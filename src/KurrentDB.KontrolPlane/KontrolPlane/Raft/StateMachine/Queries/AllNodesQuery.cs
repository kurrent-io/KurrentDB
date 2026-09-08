// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.Raft.StateMachine.Queries;

[StructLayout(LayoutKind.Auto)]
internal readonly struct AllNodesQuery : IQuery<ValueTuple<string>, PersistentDatabaseNode> {
	public static ReadOnlySpan<byte> CommandText => "SELECT address, role, is_leader, version, client_api_addr, replication_addr, instance_id, client_tcp_port, client_tcp_is_secure FROM node WHERE database_id=$1;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1,
	};

	public static PersistentDatabaseNode Parse(ref DataChunk.Row row) => new() {
		Address = row.ReadBlob().ToEndPoint(),
		Role = (DatabaseNodeRole)row.ReadInt32(),
		IsLeader = row.ReadBoolean(),
		Version = row.ReadString(),
		ClientApi = row.ReadBlob().ToEndPointOrNull(),
		Replication = row.ReadBlob().ToEndPoint(),
		InstanceId = Unsafe.BitCast<UInt128, Guid>(row.ReadUInt128()),
		ClientTcpApiPort = row.ReadInt32(),
		ClientTcpApiIsSecure = row.ReadBoolean(),
	};
}
