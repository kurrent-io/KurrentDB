// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using KurrentDB.AutoScavenge.Domain;

namespace KurrentDB.AutoScavenge.Tests;

public class ClusterFixtures {
	public static readonly Faker<ClusterMember> ClusterMembers =
		new Faker<ClusterMember>()
			.RuleFor(m => m.InstanceId, f => f.Random.Guid())
			.RuleFor(m => m.State, _ => "Follower")
			.RuleFor(m => m.InternalHttpEndPointIp, f => f.Internet.Ip())
			.RuleFor(m => m.InternalHttpEndPointPort, f => f.Random.Int(1_024, 65_535))
			.RuleFor(m => m.IsAlive, f => f.Random.Bool())
			.RuleFor(m => m.WriterCheckpoint, f => f.Random.Long(0, 8_192));

	public static Dictionary<Guid, ClusterMember> Generate3NodesCluster() {
		var members = ClusterMembers.Generate(3);

		long maxWriterCheckpoint = 0;
		ClusterMember? leader = null;

		foreach (var member in members) {
			if (member.WriterCheckpoint <= maxWriterCheckpoint)
				continue;

			leader = member;
			maxWriterCheckpoint = member.WriterCheckpoint;
		}

		leader!.State = "Leader";

		Assert.Single(members, m => m.State == "Leader");
		Assert.Equal(leader.WriterCheckpoint, members.Max(m => m.WriterCheckpoint));

		return members.ToDictionary(m => m.InstanceId);
	}

	public static Dictionary<Guid, ClusterMember> Generate3NodesClusterWith2RoRs() {
		var members = ClusterMembers.Generate(5);

		long maxWriterCheckpoint = 0;
		ClusterMember? leader = null;

		foreach (var member in members) {
			if (member.WriterCheckpoint <= maxWriterCheckpoint)
				continue;

			leader = member;
			maxWriterCheckpoint = member.WriterCheckpoint;
		}

		leader!.State = "Leader";

		var rorCount = 0;
		foreach (var member in members) {
			if (member.InstanceId == leader.InstanceId)
				continue;

			member.IsReadOnlyReplica = true;
			rorCount++;
			if (rorCount >= 2)
				break;
		}

		Assert.Equal(2, members.Count(m => m.IsReadOnlyReplica));
		Assert.Single(members, m => m.State == "Leader");
		Assert.Equal(leader.WriterCheckpoint, members.Max(m => m.WriterCheckpoint));

		return members.ToDictionary(m => m.InstanceId);
	}

	public static NodeId GenerateNodeId(int nodeNumber) =>
		new($"https://node{nodeNumber}", 2113);

	public static ClusterMember GenerateSingleNode() {
		var node = ClusterMembers.Generate(1).Single()!;
		node.State = "Leader";
		return node;
	}

	public static ClusterMember GetLeader(IEnumerable<ClusterMember> members) {
		return members.Single(m => m.State == "Leader");
	}

	// Returns the first follower we found in the cluster.
	public static ClusterMember GetFollower(IEnumerable<ClusterMember> members) {
		return members.First(m => m.State == "Follower");
	}

	// FIXME: This approach won't work if another node is on part with the leader. We ought to prioritize the follower
	// node in that case.
	public static ClusterMember GetFarthestNodeInReplication(IEnumerable<ClusterMember> members) {
		return members.OrderBy(m => m.WriterCheckpoint).First();
	}
}
