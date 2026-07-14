// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

[StructLayout(LayoutKind.Auto)]
internal readonly struct AllNodesQuery : IQuery<ValueTuple<string>, (EndPoint Address, DatabaseNodeRole Role, bool IsLeader, string Version, EndPoint? ClientApi, EndPoint Replication, Guid InstanceId)> {
	public static ReadOnlySpan<byte> CommandText => "SELECT address, role, is_leader, version, client_api_addr, replication_addr, instance_id FROM node WHERE database_id=$1;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1,
	};

	public static (EndPoint Address, DatabaseNodeRole Role, bool IsLeader, string Version, EndPoint? ClientApi, EndPoint Replication, Guid InstanceId)
		Parse(ref DataChunk.Row row) => new() {
		Address = row.ReadBlob().ToEndPoint(),
		Role = (DatabaseNodeRole)row.ReadInt32(),
		IsLeader = row.ReadBoolean(),
		Version = row.ReadString(),
		ClientApi = row.ReadBlob().ToEndPointOrNull(),
		Replication = row.ReadBlob().ToEndPoint(),
		InstanceId = Unsafe.BitCast<UInt128, Guid>(row.ReadUInt128()),
	};
}
