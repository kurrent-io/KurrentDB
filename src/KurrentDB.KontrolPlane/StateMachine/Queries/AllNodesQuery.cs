// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

[StructLayout(LayoutKind.Auto)]
internal readonly struct AllNodesQuery : IQuery<ValueTuple<string>, (EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> {
	public static ReadOnlySpan<byte> CommandText => "SELECT (address, is_read_only_replica, is_leader) FROM node WHERE database_id=?;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1,
	};

	public static (EndPoint Address, bool IsReadOnlyReplica, bool IsLeader) Parse(ref DataChunk.Row row) => new() {
		Address = row.ReadBlob().ToEndPoint(),
		IsReadOnlyReplica = row.ReadBoolean(),
		IsLeader = row.ReadBoolean(),
	};
}
