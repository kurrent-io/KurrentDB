// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct DatabaseQuery : IQuery<ValueTuple<string>, (string Description, ulong Epoch)> {
	public static ReadOnlySpan<byte> CommandText => "SELECT description, epoch FROM database WHERE id = ?;"u8;

	public static StatementBindingResult Bind(in ValueTuple<string> args, PreparedStatement source) => new(source) {
		args.Item1,
	};

	public static (string Description, ulong Epoch) Parse(ref DataChunk.Row row) => new() {
		Description = row.ReadString(),
		Epoch = row.ReadUInt64(),
	};
}
