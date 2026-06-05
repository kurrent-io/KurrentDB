// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;

namespace KurrentDB.KontrolPlane.StateMachine.LogEntries;

[StructLayout(LayoutKind.Auto)]
internal readonly struct ProtobufLogEntry<T>(T entry) : IInputLogEntry
	where T : class, ILogEntry<T> {

	ValueTask IDataTransferObject.WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
		=> entry.WriteToAsync(writer, token);

	bool IDataTransferObject.IsReusable => true;

	long? IDataTransferObject.Length => null;

	int? IRaftLogEntry.CommandId => T.TypeId;

	public required long Term { get; init; }

	public object? Context { get; init; }
}
