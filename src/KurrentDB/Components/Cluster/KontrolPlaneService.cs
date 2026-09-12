// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using KurrentDB.KontrolPlane;
using KurrentDB.KontrolPlane.Raft;

namespace KurrentDB.Components.Cluster;

public sealed class KontrolPlaneService(IKontroller? kontroller) {
	private readonly IRaftKontroller? _raftKontroller = kontroller as IRaftKontroller;

	public bool IsRaftKontrolPlaneNode => _raftKontroller is not null;

	public KontrollerClusterInfo? GetClusterInfo() => _raftKontroller?.GetClusterInfo();
}
