// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct CommandInfo(long Index, long Term) {
	public CommandInfo(in LogEntry entry)
		: this(entry.Index, entry.Term) {
	}
}
