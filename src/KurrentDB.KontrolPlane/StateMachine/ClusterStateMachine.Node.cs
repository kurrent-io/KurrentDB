// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane.StateMachine;

partial class ClusterStateMachine {
	private bool AddDatabaseNode(LogEntries.AddOrUpdateDatabaseNode entry) {
		var stateCopy = CurrentState;

		if (!stateCopy.Databases.TryGetValue(entry.DatabaseId, out var database)) {
			if (entry.DatabaseId is not IDatabase.MainDatabaseId)
				return false;

			// Add main database automatically
			stateCopy = stateCopy.AddMainDatabase(out database);
		}

		if (database.Nodes.Find(entry.Address, out var index) is { } node) {
			// update existing node
			node = new DatabaseNode(node) { IsReadOnlyReplica = entry.IsReadOnlyReplica };
			stateCopy = stateCopy.UpdateDatabaseNode(entry.DatabaseId, node, index);
		} else {
			// create new node
			node = new DatabaseNode { Address = entry.Address, IsReadOnlyReplica = entry.IsReadOnlyReplica };
			stateCopy = stateCopy.AddDatabaseNode(entry.DatabaseId, node);
		}

		CurrentState = stateCopy;
		return true;
	}

	private bool RemoveDatabaseNode(LogEntries.RemoveDatabaseNode entry) {
		var stateCopy = CurrentState;

		if (!stateCopy.Databases.ContainsKey(entry.DatabaseId))
			return false;

		CurrentState = stateCopy.RemoveDatabaseNode(entry.DatabaseId, entry.Address);
		return true;
	}
}
