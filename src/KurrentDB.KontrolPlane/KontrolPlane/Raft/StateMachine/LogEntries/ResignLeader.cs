// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane.Raft.StateMachine.LogEntries;

partial class ResignLeader : ILogEntry<ResignLeader>, IDatabaseModificationCommand {
	public const int TypeId = 6;

	static int ILogEntry.TypeId => TypeId;
}
