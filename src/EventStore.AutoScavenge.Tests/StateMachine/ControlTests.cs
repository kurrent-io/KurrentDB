// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Scavengers;

namespace EventStore.AutoScavenge.Tests.StateMachine;

// Home of tests related to pause/resume commands of the auto scavenge process.
public class ControlTests {
	/// The test verifies that we are able to stop an auto scavenge process in normal condition. The test is a success
	/// if the simulator ends up emitting an auto scavenge process paused event.
	[Fact]
	public async Task ShouldPauseTheAutoScavengeProcessWhenScavengingNode() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var clusterMembersByNodeId = clusterMembers.ToDictionary(
			x => x.Value.ToNodeId(),
			x => x.Value);
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId())
			.ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();
		var followers = activeNodes.Except([leader.ToNodeId()]).ToList();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = activeNodes.ToList(),
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
		]);

		simulator.Scavenger.RegisterScavengeFor(Guid.Parse("5cabeeee-0000-0000-0000-000000000001"), clusterMembersByNodeId[followers[0]], ScavengeStatus.InProgress);

		var pauseProcessTask = new TaskCompletionSource<Response<Unit>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.PauseProcess(resp => pauseProcessTask.SetResult(resp)),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.PauseRequested>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),

			Matches.MatchEvent<Events.Paused>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),
		]);

		var response = await pauseProcessTask.Task;

		Assert.True(response.IsAccepted(out _));
	}

	[Fact]
	public async Task ShouldPauseTheAutoScavengeProcessWhenNotScavengingNode() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var activeNodes = clusterMembers.Values.Select(m => m.InstanceId)
			.ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();
		var followers = activeNodes.Except([leader.InstanceId]).ToList();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},
		]);

		simulator.Scavenger.RegisterScavengeFor(Guid.Parse("5cabeeee-0000-0000-0000-000000000001"), clusterMembers[followers[0]], ScavengeStatus.InProgress);

		var pauseProcessTask = new TaskCompletionSource<Response<Unit>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.PauseProcess(resp => pauseProcessTask.SetResult(resp)),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.Paused>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),
		]);

		var response = await pauseProcessTask.Task;

		Assert.True(response.IsSuccessful(out _));
	}

	[Fact]
	public async Task ShouldPauseTheAutoScavengeProcessWhenPossiblyScavengingNode() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId())
			.ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();
		var followers = activeNodes.Except([leader.ToNodeId()]).ToList();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = activeNodes.ToList(),
				StartedAt = simulator.Now,
			},

			new Events.NodeDesignated {
				NodeId = followers[0],
				DesignatedAt = simulator.Now,
			},
		]);

		simulator.Scavenger.Enabled = false;

		var pauseProcessTask = new TaskCompletionSource<Response<Unit>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.PauseProcess(resp => pauseProcessTask.SetResult(resp)),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.PauseRequested>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),

			Matches.MatchEvent<Events.Paused>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),
		]);

		var response = await pauseProcessTask.Task;

		Assert.True(response.IsAccepted(out _));
	}

	/// The test verifies that we are able to resume a paused auto scavenge process in normal condition. The test is
	/// a success if the simulator ends up emitting an auto scavenge process resumed event.
	[Fact]
	public async Task ShouldResumePausedAutoScavengeProcess() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId())
			.ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();
		var followers = activeNodes.Except([leader.ToNodeId()]).ToList();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = activeNodes.ToList(),
				StartedAt = simulator.Now,
			},

			new Events.NodeDesignated {
				NodeId = followers[0],
				DesignatedAt = simulator.Now,
			},

			new Events.Paused() {
				PausedAt = simulator.Now,
			},
		]);

		simulator.Reacts([
			Reacts.OnEvent<Events.NodeScavengeStarted>(_ => simulator.EnqueueCommands(Command.Suspend)),
		]);

		var pauseProcessTask = new TaskCompletionSource<Response<Unit>>();

		simulator.EnqueueCommands(
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.ResumeProcess(pauseProcessTask.SetResult)
		);
		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.Resumed>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),

			Matches.MatchEvent<Events.NodeDesignated>(),

			// Testament that the auto scavenge process started a node scavenge on the designated node.
			Matches.MatchEvent<Events.NodeScavengeStarted>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.DesignatedNode, snapshot.NewState.DesignatedNode);
				Assert.Equal(@event.NodeId, snapshot.NewState.DesignatedNode);
				Assert.True(simulator.Scavenger.Scavenges.ContainsKey(@event.ScavengeId));
			}),
		]);

		var response = await pauseProcessTask.Task.WaitAsync(TimeSpan.FromSeconds(2));

		Assert.True(response.IsSuccessful(out _));
	}

	/// The test verifies that we are able to pause an auto scavenge process when the node being scavenged becomes
	/// temporarily unavailable
	[Fact]
	public async Task ShouldPauseTheAutoScavengeProcessWhenNodeBeingScavengedIsTemporarilyUnavailable() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var clusterMembersByNodeId = clusterMembers.ToDictionary(
			x => x.Value.ToNodeId(),
			x => x.Value);
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId())
			.ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();
		var followers = activeNodes.Except([leader.ToNodeId()]).ToList();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = activeNodes.ToList(),
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
			}
		]);

		simulator.Scavenger.RegisterScavengeFor(Guid.Parse("5cabeeee-0000-0000-0000-000000000001"), clusterMembersByNodeId[followers[0]], ScavengeStatus.InProgress);
		simulator.Scavenger.Pausable = false;

		var pauseProcessTask = new TaskCompletionSource<Response<Unit>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.PauseProcess(resp => pauseProcessTask.SetResult(resp)),
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList())
		]);

		var gossipCount = 0;
		simulator.Reacts(
			Reacts.After<Commands.ReceiveGossip>(@event => {
				if (++gossipCount == 3)
					simulator.Scavenger.Pausable = true;
			}));

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.PauseRequested>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),
			Matches.MatchEvent<Events.Paused>()
		]);

		var response = await pauseProcessTask.Task;

		Assert.True(response.IsAccepted(out _));
	}

	/// The test verifies that we are able to pause an auto scavenge process when the node being scavenged is removed
	/// from gossip, then replaced by a new node.
	[Fact]
	public async Task ShouldPauseTheAutoScavengeProcessWhenNodeBeingScavengedIsReplacedInGossip() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var clusterMembersByNodeId = clusterMembers.ToDictionary(
			x => x.Value.ToNodeId(),
			x => x.Value);
		var activeNodes = clusterMembers.Values.Select(m => m.ToNodeId())
			.ToHashSet();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);
		var clusterScavengeId = Guid.NewGuid();
		var followers = activeNodes.Except([leader.ToNodeId()]).ToList();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},

			new Events.ClusterScavengeStarted {
				ClusterScavengeId = clusterScavengeId,
				Members = activeNodes.ToList(),
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
			}
		]);

		simulator.Scavenger.RegisterScavengeFor(Guid.Parse("5cabeeee-0000-0000-0000-000000000001"), clusterMembersByNodeId[followers[0]], ScavengeStatus.InProgress);
		simulator.Scavenger.Pausable = false;

		var pauseProcessTask = new TaskCompletionSource<Response<Unit>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.PauseProcess(resp => pauseProcessTask.SetResult(resp)),
		]);

		// remove the node being scavenged from the list of cluster members
		var scavengingNode = clusterMembers.Values.First(x => x.ToNodeId() == followers[0]);
		clusterMembers.Remove(scavengingNode.InstanceId);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
		]);

		// replace the node that was being scavenged by a new node
		var newNode = ClusterFixtures.ClusterMembers.Generate(1).First();
		clusterMembers.Add(newNode.InstanceId, newNode);

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.PauseRequested>((@event, snapshot) => {
				Assert.Equal(snapshot.PrevState.ClusterScavengeId, snapshot.NewState.ClusterScavengeId);
			}),
			Matches.MatchEvent<Events.ClusterMembersChanged>(),
			Matches.MatchEvent<Events.Paused>()
		]);

		var response = await pauseProcessTask.Task;

		Assert.True(response.IsAccepted(out _));
	}
}
