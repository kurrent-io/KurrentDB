// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine.Queries;

[StructLayout(LayoutKind.Auto)]
internal readonly struct AllDatabasesQuery : IQuery<string> {
	public static ReadOnlySpan<byte> CommandText => "SELECT id FROM database;"u8;

	public static string Parse(ref DataChunk.Row row) => row.ReadString();
}
