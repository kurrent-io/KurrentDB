// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

[StructLayout(LayoutKind.Auto)]
internal readonly struct LeaderQuery : IQuery<ValueTuple<string>, (EndPoint Address, ulong Epoch, bool IsReadOnlyReplica)> {
	public static ReadOnlySpan<byte> CommandText => """
	                                                SELECT n.address, d.epoch, n.is_read_only_replica
	                                                FROM node n
	                                                JOIN database d ON n.database_id = d.id
	                                                WHERE n.is_leader=true AND d.id=?
	                                                """u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1
	};

	public static (EndPoint Address, ulong Epoch, bool IsReadOnlyReplica) Parse(ref DataChunk.Row row) => new() {
		Address = row.ReadBlob().ToEndPoint(),
		Epoch = row.ReadUInt64(),
		IsReadOnlyReplica = row.ReadBoolean(),
	};
}
