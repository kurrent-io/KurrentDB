// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.AutoScavenge.Domain;
using KurrentDB.AutoScavenge.Scavengers;

namespace KurrentDB.AutoScavenge.Tests.StateMachine;

public class CompleteProcessTests {

	/// This test verifies that with a pre-existing configuration, the state machine detects that an auto
	/// scavenge process is due. In this scenario, everything goes well like remote node scavenges for
	/// example. The test should end with the leader node resigning because it's the only node left that needs to be
	/// scavenged at the end of the process. That test simulates a 3 node cluster.
	[Fact]
	public async Task ShouldCompleteAutoScavengeProcessToLeaderResignation() {
		var tokenSource = new CancellationTokenSource();
		var simulator = new Simulator(3);
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId()).ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.FastForward(TimeSpan.FromHours(1)),
		]);

		// Sets an auto scavenge configuration event for when the state machine will reload its configuration.
		simulator.AddEventsToStorage(new Events.ConfigurationUpdated {
			Schedule = Schedule.EveryHour,
			UpdatedAt = simulator.Now,
		});

		simulator.Reacts(
			Reacts.OnEvent<Events.NodeScavengeStarted>(@event => {
				simulator.Scavenger.SetScavengeStatus(@event.ScavengeId, ScavengeStatus.Success);
				simulator.EnqueueCommands(Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()));
			}));

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ClusterScavengeStarted>((@event, snapshot) => {
				Assert.NotEqual(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.Members.ToHashSet(), snapshot.NewState.ClusterMembers);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(AutoScavengeStatus.ContinuingClusterScavenge, snapshot.NewState.Status);

				var needingScavenge = snapshot.NewState.NodesToScavenge.ToHashSet();
				Assert.Equal(activeNodes, needingScavenge);
				Assert.Equal(3, needingScavenge.Count);
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				var node = ClusterFixtures.GetFarthestNodeInReplication(
					clusterMembers.Values.Where(m => needingScavenge.Contains(m.ToNodeId())));
				Assert.Equal(snapshot.NewState.DesignatedNode, node.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.NotEqual(snapshot.PrevState.NodeScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.ScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);

				var scavengingNode = simulator.Scavenger.Scavenges[@event.ScavengeId];

				Assert.Equal(scavengingNode.Host, @event.NodeId.Address);
				Assert.Equal(scavengingNode.Port, @event.NodeId.Port);
			}),

			Matches.MatchEvent<Events.NodeScavengeCompleted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.PrevState.DesignatedNode);
				Assert.Equal(@event.CompletedAt, snapshot.Time);
				Assert.Null(snapshot.NewState.DesignatedNode);
				Assert.Equal(2, snapshot.NewState.NodesToScavenge.Count());
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				var node = ClusterFixtures.GetFarthestNodeInReplication(
					clusterMembers.Values.Where(m => needingScavenge.Contains(m.ToNodeId())));
				Assert.Equal(snapshot.NewState.DesignatedNode, node.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.NotEqual(snapshot.PrevState.NodeScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.ScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);

				var scavengingNode = simulator.Scavenger.Scavenges[@event.ScavengeId];

				Assert.Equal(scavengingNode.Host, @event.NodeId.Address);
				Assert.Equal(scavengingNode.Port, @event.NodeId.Port);
			}),

			Matches.MatchEvent<Events.NodeScavengeCompleted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.PrevState.DesignatedNode);
				Assert.Equal(@event.CompletedAt, snapshot.Time);
				Assert.Null(snapshot.NewState.DesignatedNode);
				Assert.Single(snapshot.NewState.NodesToScavenge);
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				Assert.Single(needingScavenge);
				Assert.Equal(snapshot.NewState.DesignatedNode, leader.ToNodeId());
			}),
		]);

		Assert.True(simulator.OperationsClient.ResignationIssued);
	}

	/// The test configures the simulator so the state machine detects there is only one node left that needs to be
	/// scavenged. The test should end with that last node scavenged and the process marked as completed.
	[Fact]
	public async Task ShouldDetectThereIsOneNodeLeftThatNeedsScavengeThenCompleteTheProcess() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var clusterMembersByNodeId = clusterMembers.ToDictionary(
			x => x.Value.ToNodeId(),
			x => x.Value);
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();
		var followers = clusterMembersByNodeId.Keys.Except([leader.ToNodeId()]).ToList();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = clusterMembersByNodeId.Keys.ToList(),
				StartedAt = simulator.Now,
			},

			new Events.NodeDesignated {
				NodeId = followers[0],
				DesignatedAt = simulator.Now,
			},

			new Events.NodeScavengeStarted {
				NodeId = followers[0],
				ScavengeId = Guid.Parse("5cabeeee-0000-0000-0000-000000000001"),
				StartedAt = simulator.Now,
			},

			new Events.NodeScavengeCompleted {
				NodeId = followers[0],
				CompletedAt = simulator.Now,
				Result = "Success"
			},

			new Events.NodeDesignated {
				NodeId = followers[1],
				DesignatedAt = simulator.Now,
			},

			new Events.NodeScavengeStarted {
				NodeId = followers[1],
				ScavengeId = Guid.Parse("5cabeeee-0000-0000-0000-000000000002"),
				StartedAt = simulator.Now,
			},

			new Events.NodeScavengeCompleted {
				NodeId = followers[1],
				CompletedAt = simulator.Now,
				Result = "Success"
			},

			new Events.NodeDesignated {
				NodeId = leader.ToNodeId(),
				DesignatedAt = simulator.Now,
			},
		]);

		leader.State = "Follower";
		clusterMembersByNodeId[followers[0]].State = "Leader";

		simulator.EnqueueCommands([
			Command.ReceiveGossip(clusterMembersByNodeId[followers[0]].InstanceId, clusterMembers.Values.ToList())
		]);

		simulator.Reacts(
			Reacts.OnEvent<Events.NodeScavengeStarted>(@event => {
				simulator.Scavenger.SetScavengeStatus(@event.ScavengeId, ScavengeStatus.Success);
				simulator.EnqueueCommands(Command.ReceiveGossip(clusterMembersByNodeId[followers[0]].InstanceId, clusterMembers.Values.ToList()));
			}));

		await simulator.Run(tokenSource.Token);

		var previousLeader = leader;

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, _) => {
				Assert.Equal(@event.NodeId, previousLeader.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeCompleted>((@event, _) => {
				Assert.Equal(@event.NodeId, previousLeader.ToNodeId());
			}),

			Matches.MatchEvent<Events.ClusterScavengeCompleted>((@event, _) => {
				Assert.Equal(@event.ClusterScavengeId, clusterScavengeId);
				Assert.Equal(@event.Members, clusterMembersByNodeId.Keys.ToList());
			})
		]);
	}

	/// The test verifies that a single node database is able to complete an auto scavenge process successfully.
	[Fact]
	public async Task ShouldCompleteAutoScavengeProcessOnASingleNodeSetUp() {
		var simulator = new Simulator(1);
		var tokenSource = new CancellationTokenSource();
		var singleNode = ClusterFixtures.GenerateSingleNode();

		simulator.AddEventsToStorage(new Events.ConfigurationUpdated {
			Schedule = Schedule.EveryHour,
			UpdatedAt = simulator.Now,
		});

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.FastForward(TimeSpan.FromHours(1))
		]);

		simulator.Reacts(
			Reacts.OnEvent<Events.NodeScavengeStarted>(@event => {
				simulator.Scavenger.SetScavengeStatus(@event.ScavengeId, ScavengeStatus.Success);
				simulator.EnqueueCommands(Command.ReceiveGossip(singleNode.InstanceId, [singleNode]));
			}));

		await simulator.Run(tokenSource.Token).WaitAsync(TimeSpan.FromSeconds(2));

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ClusterScavengeStarted>((_, snapshot) => {
				Assert.Equal(AutoScavengeStatus.ContinuingClusterScavenge, snapshot.NewState.Status);
			}),
			Matches.MatchEvent<Events.NodeDesignated>(),
			Matches.MatchEvent<Events.NodeScavengeStarted>(),
			Matches.MatchEvent<Events.NodeScavengeCompleted>(),
			Matches.MatchEvent<Events.ClusterScavengeCompleted>(),
		]);
	}

	[Fact]
	public async Task ShouldCompleteAutoScavengeProcessToLeaderResignationWithRoR() {
		var tokenSource = new CancellationTokenSource();
		var simulator = new Simulator(3);
		var clusterMembers = ClusterFixtures.Generate3NodesClusterWith2RoRs();
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId()).ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.FastForward(TimeSpan.FromHours(1)),
		]);

		// Sets an auto scavenge configuration event for when the state machine will reload its configuration.
		simulator.AddEventsToStorage(new Events.ConfigurationUpdated {
			Schedule = Schedule.EveryHour,
			UpdatedAt = simulator.Now,
		});

		simulator.Reacts(
			Reacts.OnEvent<Events.NodeScavengeStarted>(@event => {
				simulator.Scavenger.SetScavengeStatus(@event.ScavengeId, ScavengeStatus.Success);
				simulator.EnqueueCommands(Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()));
			}));

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ClusterScavengeStarted>((@event, snapshot) => {
				Assert.NotEqual(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.Members.ToHashSet(), snapshot.NewState.ClusterMembers);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(AutoScavengeStatus.ContinuingClusterScavenge, snapshot.NewState.Status);

				var needingScavenge = snapshot.NewState.NodesToScavenge.ToHashSet();
				Assert.Equal(activeNodes, needingScavenge);
				Assert.Equal(5, needingScavenge.Count);
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				var node = ClusterFixtures.GetFarthestNodeInReplication(
					clusterMembers.Values.Where(m => needingScavenge.Contains(m.ToNodeId())));
				Assert.Equal(snapshot.NewState.DesignatedNode, node.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.NotEqual(snapshot.PrevState.NodeScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.ScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);

				var scavengingNode = simulator.Scavenger.Scavenges[@event.ScavengeId];

				Assert.Equal(scavengingNode.Host, @event.NodeId.Address);
				Assert.Equal(scavengingNode.Port, @event.NodeId.Port);
			}),

			Matches.MatchEvent<Events.NodeScavengeCompleted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.PrevState.DesignatedNode);
				Assert.Equal(@event.CompletedAt, snapshot.Time);
				Assert.Null(snapshot.NewState.DesignatedNode);
				Assert.Equal(4, snapshot.NewState.NodesToScavenge.Count());
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				var node = ClusterFixtures.GetFarthestNodeInReplication(
					clusterMembers.Values.Where(m => needingScavenge.Contains(m.ToNodeId())));
				Assert.Equal(snapshot.NewState.DesignatedNode, node.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.NotEqual(snapshot.PrevState.NodeScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.ScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);

				var scavengingNode = simulator.Scavenger.Scavenges[@event.ScavengeId];

				Assert.Equal(scavengingNode.Host, @event.NodeId.Address);
				Assert.Equal(scavengingNode.Port, @event.NodeId.Port);
			}),

			Matches.MatchEvent<Events.NodeScavengeCompleted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.PrevState.DesignatedNode);
				Assert.Equal(@event.CompletedAt, snapshot.Time);
				Assert.Null(snapshot.NewState.DesignatedNode);
				Assert.Equal(3, snapshot.NewState.NodesToScavenge.Count());
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				var node = ClusterFixtures.GetFarthestNodeInReplication(
					clusterMembers.Values.Where(m => needingScavenge.Contains(m.ToNodeId())));
				Assert.Equal(snapshot.NewState.DesignatedNode, node.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.NotEqual(snapshot.PrevState.NodeScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.ScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);

				var scavengingNode = simulator.Scavenger.Scavenges[@event.ScavengeId];

				Assert.Equal(scavengingNode.Host, @event.NodeId.Address);
				Assert.Equal(scavengingNode.Port, @event.NodeId.Port);
			}),

			Matches.MatchEvent<Events.NodeScavengeCompleted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.PrevState.DesignatedNode);
				Assert.Equal(@event.CompletedAt, snapshot.Time);
				Assert.Null(snapshot.NewState.DesignatedNode);
				Assert.Equal(2, snapshot.NewState.NodesToScavenge.Count());
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				var node = ClusterFixtures.GetFarthestNodeInReplication(
					clusterMembers.Values.Where(m => needingScavenge.Contains(m.ToNodeId())));
				Assert.Equal(snapshot.NewState.DesignatedNode, node.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.NotEqual(snapshot.PrevState.NodeScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.ScavengeId, snapshot.NewState.NodeScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.StartedAt, snapshot.Time);
				Assert.Equal(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);

				var scavengingNode = simulator.Scavenger.Scavenges[@event.ScavengeId];

				Assert.Equal(scavengingNode.Host, @event.NodeId.Address);
				Assert.Equal(scavengingNode.Port, @event.NodeId.Port);
			}),

			Matches.MatchEvent<Events.NodeScavengeCompleted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.PrevState.DesignatedNode);
				Assert.Equal(@event.CompletedAt, snapshot.Time);
				Assert.Null(snapshot.NewState.DesignatedNode);
				Assert.Single(snapshot.NewState.NodesToScavenge);
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.NotEqual(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.DesignatedAt, snapshot.Time);

				var needingScavenge = snapshot.PrevState.NodesToScavenge.ToHashSet();
				Assert.Single(needingScavenge);
				Assert.Equal(snapshot.NewState.DesignatedNode, leader.ToNodeId());
			}),
		]);

		Assert.True(simulator.OperationsClient.ResignationIssued);
	}
}
