// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane.Raft.StateMachine.LogEntries;

partial class RemoveDatabase : ILogEntry<RemoveDatabase> {
	public const int TypeId = 3;

	static int ILogEntry.TypeId => TypeId;
}
