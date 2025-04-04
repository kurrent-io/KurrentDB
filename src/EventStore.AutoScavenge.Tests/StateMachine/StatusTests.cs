// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;

namespace EventStore.AutoScavenge.Tests.StateMachine;

/// <summary>
/// Home of the get-status command tests.
/// </summary>
public class StatusTests {
	/// Verifies that the status is retrieved when the state machine is already loaded.
	[Fact]
	public async Task ShouldRetrieveStatusWhenLoaded() {
		var simulator = new Simulator(3);
		var tokenSource = new CancellationTokenSource();
		var clusterMembers = ClusterFixtures.Generate3NodesCluster();
		var leader = ClusterFixtures.GetLeader(clusterMembers.Values);

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},
		]);

		var statusTask = new TaskCompletionSource<Response<AutoScavengeStatusResponse>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(leader.InstanceId, clusterMembers.Values.ToList()),
			Command.GetStatus(statusTask),
		]);

		await simulator.Run(tokenSource.Token);

		var response = await statusTask.Task;

		Assert.True(response.IsSuccessful(out var result));
		Assert.Equal(AutoScavengeStatusResponse.Status.Waiting, result.State);
		Assert.Equal(Schedule.EveryHour.ToString(), result.Schedule!.ToString());
		Assert.Equal(TimeSpan.FromHours(1), result.TimeUntilNextCycle);
	}

	/// Verifies that the status is retrieved when the state machine is not loaded nor configured
	[Fact]
	public async Task ShouldRetrieveStatusWhenNotLoadedNorConfigured() {
		var simulator = new Simulator(1);
		var singleNode = ClusterFixtures.GenerateSingleNode();
		var tokenSource = new CancellationTokenSource();
		var statusTask = new TaskCompletionSource<Response<AutoScavengeStatusResponse>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.GetStatus(statusTask),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([]);
		var response = await statusTask.Task;

		Assert.True(response.IsSuccessful(out var result));
		Assert.Equal(AutoScavengeStatusResponse.Status.NotConfigured, result.State);
		Assert.Null(result.Schedule);
		Assert.Null(result.TimeUntilNextCycle);
	}

	/// Verifies that the status is retrieved when the state machine tried to load its configuration once but didn't
	/// find anything.
	[Fact]
	public async Task ShouldRetrieveStatusWhenLoadedButNotConfigured() {
		var simulator = new Simulator(1);
		var singleNode = ClusterFixtures.GenerateSingleNode();
		var tokenSource = new CancellationTokenSource();
		var statusTask = new TaskCompletionSource<Response<AutoScavengeStatusResponse>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.GetStatus(statusTask),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([]);
		var response = await statusTask.Task;

		Assert.True(response.IsSuccessful(out var result));
		Assert.Equal(AutoScavengeStatusResponse.Status.NotConfigured, result.State);
		Assert.Null(result.Schedule);
		Assert.Null(result.TimeUntilNextCycle);
	}

	[Fact]
	public async Task when_paused_while_idle_next_cycle_is_schedule_from_now() {
		var simulator = new Simulator(clusterSize: 1);
		var singleNode = ClusterFixtures.GenerateSingleNode();
		var tokenSource = new CancellationTokenSource();
		var statusTask = new TaskCompletionSource<Response<AutoScavengeStatusResponse>>();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},
			new Events.Paused() {
				PausedAt = simulator.Now,
			},
		]);
		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.FastForward(TimeSpan.FromMinutes(65)),
			Command.GetStatus(statusTask),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([]);
		var response = await statusTask.Task;

		Assert.True(response.IsSuccessful(out var result));
		Assert.Equal(AutoScavengeStatusResponse.Status.Paused, result.State);
		Assert.Equal(Schedule.EveryHour.ToString(), result.Schedule!.ToString());
		Assert.Equal(TimeSpan.FromMinutes(55), result.TimeUntilNextCycle);
	}

	[Fact]
	public async Task when_paused_while_scavenging_next_cycle_is_immediate() {
		var simulator = new Simulator(clusterSize: 1);
		var singleNode = ClusterFixtures.GenerateSingleNode();
		var tokenSource = new CancellationTokenSource();
		var statusTask = new TaskCompletionSource<Response<AutoScavengeStatusResponse>>();

		simulator.AddEventsToStorage([
			new Events.ConfigurationUpdated {
				Schedule = Schedule.EveryHour,
				UpdatedAt = simulator.Now,
			},
			new Events.ClusterScavengeStarted {
				ClusterScavengeId = Guid.NewGuid(),
				Members = [
					singleNode.ToNodeId(),
				],
				StartedAt = simulator.Now,
			},
			new Events.Paused() {
				PausedAt = simulator.Now,
			},
		]);
		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.FastForward(TimeSpan.FromMinutes(65)),
			Command.GetStatus(statusTask),
		]);

		simulator.Time.FastForward(TimeSpan.FromMinutes(65));
		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([]);
		var response = await statusTask.Task;

		Assert.True(response.IsSuccessful(out var result));
		Assert.Equal(AutoScavengeStatusResponse.Status.Paused, result.State);
		Assert.Equal(Schedule.EveryHour.ToString(), result.Schedule!.ToString());
		Assert.Equal(TimeSpan.Zero, result.TimeUntilNextCycle);
	}

	// TODO - We also probably need to check the state transitions to ascertain we are moving the way we expect.
}
