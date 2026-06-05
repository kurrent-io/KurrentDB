// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane.StateMachine.LogEntries;

partial class AddOrUpdateDatabaseNode : ILogEntry<AddOrUpdateDatabaseNode> {
	public const int TypeId = 0;

	static int ILogEntry.TypeId => TypeId;
}
