// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.IO.Log;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;

namespace KurrentDB.KontrolPlane.Raft;

/// <summary>
/// One node of the Kontrol Plane's Raft cluster.
/// </summary>
/// <remarks>
/// The members mirror <see cref="IRaftClusterMember"/>, of which this is an inert snapshot.
/// </remarks>
public readonly record struct KontrollerNodeInfo(
	EndPoint EndPoint,
	bool IsLeader,
	bool IsRemote,
	ClusterMemberStatus Status) {
	public KontrollerNodeInfo(IRaftClusterMember member)
		: this(member.EndPoint, member.IsLeader, member.IsRemote, member.Status) {
	}
}

/// <summary>
/// One Kontroller's view of the Kontrol Plane cluster.
/// </summary>
public readonly record struct KontrollerClusterInfo(
	long Term,
	long LastEntryIndex,
	long LastCommittedEntryIndex,
	IReadOnlyList<KontrollerNodeInfo> Nodes) {
	public KontrollerClusterInfo(long term, IAuditTrail log, IEnumerable<IRaftClusterMember> members)
		: this(term,
			log.LastEntryIndex,
			log.LastCommittedEntryIndex,
			[.. members.Select(static member => new KontrollerNodeInfo(member))]) {
	}
}
