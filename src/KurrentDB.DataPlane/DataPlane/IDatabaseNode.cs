// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents database node.
/// </summary>
public interface IDatabaseNode {
	// Task<CancellationToken> WaitForLeadershipAsync(CancellationToken token = default);

	event Action OnLeadershipAcquired;

	event Action OnLeadershipLost;

	ValueTask<bool> ResignLeaderAsync(CancellationToken token = default);

	// event Action<DatabaseNode[]> MembersChanged;

	event Action<DatabaseNode> OnNodeAdded;

	event Action<DatabaseNode> OnNodeRemoved;

	event Action<DatabaseNode> OnNodeChanged;
}
