// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane.StateMachine;

partial class ClusterStateMachine {
	private bool AddDatabase(LogEntries.AddOrUpdateDatabase entry) {
		var stateCopy = CurrentState;

		if (stateCopy.Databases.ContainsKey(entry.DatabaseId))
			return false;

		CurrentState = stateCopy.AddDatabase(entry.DatabaseId, new() { Description = entry.Description });
		return true;
	}

	private bool RemoveDatabase(LogEntries.RemoveDatabase entry) {
		var stateCopy = CurrentState;

		if (!stateCopy.Databases.ContainsKey(entry.DatabaseId))
			return false;

		CurrentState = stateCopy.RemoveDatabase(entry.DatabaseId);
		return true;
	}

	private bool AppointLeader(LogEntries.AppointLeader entry) {
		return TrySetCurrentState(CurrentState.AppointLeader(entry.DatabaseId, entry.ExpectedVersion, entry.Address));
	}
}
