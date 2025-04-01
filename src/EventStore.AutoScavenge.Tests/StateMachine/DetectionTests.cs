// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;
using EventStore.AutoScavenge.Scavengers;

namespace EventStore.AutoScavenge.Tests.StateMachine;

public class DetectionTests {
	/// This test verifies that the state machine can detect that there has been enough time between the last auto
	/// scavenge process to start a new one.
	[Fact]
	public async Task ShouldDetectAutoScavengeProcessIsDueFromPreviousRun() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryDay,
				UpdatedAt = simulator.Time.NowThenFastForward(TimeSpan.FromHours(1)),
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = clusterMembers.Values.Select(x => x.ToNodeId()).ToList(),
				StartedAt = simulator.Time.NowThenFastForward(TimeSpan.FromHours(1)),
			},

			new Events.ClusterScavengeCompleted {
				ClusterScavengeId = clusterScavengeId,
				Members = clusterMembers.Values.Select(x => x.ToNodeId()).ToList(),
				CompletedAt = simulator.Time.Now
			}
		]);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.FastForward(TimeSpan.FromHours(22))
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ClusterScavengeStarted>(),
			Matches.MatchEvent<Events.NodeDesignated>(),
			Matches.MatchEvent<Events.NodeScavengeStarted>()
		]);
	}

	/// Makes sure that the state machine does not start a new scavenge process if the node is not configured for
	/// auto scavenge process.
	[Fact]
	public async Task ShouldNotStartScavengeProcessIfNodeIsNotConfigured() {
		var simulator = new Simulator(1);
		var tokenSource = new CancellationTokenSource();
		var singleNode = ClusterFixtures.GenerateSingleNode();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([]);
	}

	/// Makes sure that the state machine does not start a new scavenge process immediately after the node is configured
	/// for auto scavenge process.
	[Fact]
	public async Task ShouldNotStartScavengeProcessImmediatelyAfterNodeIsConfigured() {
		var simulator = new Simulator(1);
		var tokenSource = new CancellationTokenSource();
		var singleNode = ClusterFixtures.GenerateSingleNode();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.FastForward(TimeSpan.FromHours(1)),
			Command.Configure(Schedule.EveryHour)
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ConfigurationUpdated>()
		]);
	}

	/// Makes sure that the state machine does not start a new scavenge process immediately after the node is re-configured
	/// for auto scavenge process.
	[Fact]
	public async Task ShouldNotStartScavengeProcessImmediatelyAfterNodeIsReconfigured() {
		var simulator = new Simulator(1);
		var tokenSource = new CancellationTokenSource();
		var singleNode = ClusterFixtures.GenerateSingleNode();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryDay,
				UpdatedAt = simulator.Time.Now
			}
		]);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.FastForward(TimeSpan.FromDays(1)),
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.FastForward(TimeSpan.FromHours(3)),
			Command.Configure(Schedule.EveryHour),
		]);

		simulator.Reacts(
			Reacts.OnEvent<Events.NodeScavengeStarted>(@event => {
				simulator.Scavenger.SetScavengeStatus(@event.ScavengeId, ScavengeStatus.Success);
			}));

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ClusterScavengeStarted>(),
			Matches.MatchEvent<Events.NodeDesignated>(),
			Matches.MatchEvent<Events.NodeScavengeStarted>(),
			Matches.MatchEvent<Events.NodeScavengeCompleted>(),
			Matches.MatchEvent<Events.ClusterScavengeCompleted>(),
			Matches.MatchEvent<Events.ConfigurationUpdated>()
		]);
	}

	/// When in a single node configuration, the test verifies that if we already started a node scavenge and the
	/// node reboot, we should start a new one because node id changed after reboot.
	[Fact]
	public async Task ShouldDetectInSingleNodeThatNodeIdChangedAfterRestart() {
		var simulator = new Simulator(1);
		var tokenSource = new CancellationTokenSource();
		var singleNode = ClusterFixtures.GenerateSingleNode();
		var previousClusterScavengeId = Guid.NewGuid();
		var previousScavengeId = Guid.NewGuid();
		var previousNodeId = ClusterFixtures.GenerateNodeId(1);

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = previousClusterScavengeId,
				Members = [previousNodeId],
				StartedAt = simulator.Now,
			},

			new Events.NodeDesignated {
				NodeId = previousNodeId,
				DesignatedAt = simulator.Now,
			},

			new Events.NodeScavengeStarted {
				NodeId = previousNodeId,
				ScavengeId = previousScavengeId,
				StartedAt = simulator.Now,
			}
		]);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]), // Leads to detect the topology has changed.
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ClusterMembersChanged>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.DesignatedNode, previousNodeId);
				Assert.Equal(previousClusterScavengeId, snapshot.PrevState.ClusterScavengeId);
				Assert.Equal(previousClusterScavengeId, snapshot.NewState.ClusterScavengeId);
				Assert.Equal([singleNode.ToNodeId()], @event.Added);
				Assert.Equal([previousNodeId], @event.Removed);
			}),

			Matches.MatchEvent<Events.NodeDesignated>((@event, snapshot) => {
				Assert.Equal(@event.NodeId, singleNode.ToNodeId());
			}),

			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(@event.NodeId, singleNode.ToNodeId());
				Assert.NotEqual(@event.ScavengeId, previousScavengeId);
			})
		]);
	}

	/// This test verifies that the state machine can detect that a cluster was offline during a certain period of time
	/// and that a scavenge process must not be started if one or more cycles were missed while the cluster was offline.
	[Fact]
	public async Task ShouldSkipAutoScavengeIfCycleWasMissed() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryDay,
				UpdatedAt = simulator.Time.NowThenFastForward(TimeSpan.FromDays(3)),
			}
		]);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.FastForward(TimeSpan.FromHours(1))
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([]);
	}

	/// This test verifies that the state machine can detect that a cluster was offline during a certain period of time
	/// and that a scavenge process must be resumed if there was already an ongoing one even though one or more cycles
	/// were missed while the cluster was offline.
	[Fact]
	public async Task ShouldResumeAutoScavengeEvenIfCycleWasMissed() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);

		var clusterScavengeId = Guid.NewGuid();
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId())
			.ToHashSet();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryDay,
				UpdatedAt = simulator.Time.NowThenFastForward(TimeSpan.FromDays(3)),
			},
			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = activeNodes.ToList(),
				StartedAt = simulator.Now,
			}
		]);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.FastForward(TimeSpan.FromHours(1))
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.NodeDesignated>(),
			Matches.MatchEvent<Events.NodeScavengeStarted>()
		]);
	}

	/// This test verifies that the state machine can detect that a cluster was offline during a certain period of time
	/// and that a scavenge process must be started if the next cycle was not missed while the cluster was offline.
	[Fact]
	public async Task ShouldStartAutoScavengeIfCycleWasNotMissed() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryDay,
				UpdatedAt = simulator.Time.NowThenFastForward(TimeSpan.FromHours(23)),
			}
		]);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.FastForward(TimeSpan.FromHours(1))
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ClusterScavengeStarted>(),
			Matches.MatchEvent<Events.NodeDesignated>(),
			Matches.MatchEvent<Events.NodeScavengeStarted>()
		]);
	}

	/// This test verifies that the state machine can detect that a cluster was offline during a certain period of time
	/// and that a scavenge process must not be started even if the next cycle was not missed as auto-scavenge was paused.
	[Fact]
	public async Task ShouldSkipAutoScavengeIfPausedAndCycleWasNotMissed() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryDay,
				UpdatedAt = simulator.Time.Now,
			},
			new Events.Paused {
				PausedAt = simulator.Time.NowThenFastForward(TimeSpan.FromHours(23))
			}
		]);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.FastForward(TimeSpan.FromHours(1))
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([]);
	}
}
