// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

[StructLayout(LayoutKind.Auto)]
internal readonly struct AllDatabasesQuery : IQuery<(string Id, string Description, ulong Epoch)> {
	public static ReadOnlySpan<byte> CommandText => "SELECT * FROM database;"u8;

	public static (string Id, string Description, ulong Epoch) Parse(ref DataChunk.Row row) => new() {
		Id = row.ReadString(),
		Description = row.ReadString(),
		Epoch = row.ReadUInt64(),
	};
}
