// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge.Tests.StateMachine;

public class ConfigurationTests {

	/// We check that a user can set up the auto scavenge process when there is no pre-existing configuration. The
	/// state machine must be updated accordingly and the configuration persisted.
	[Fact]
	public async Task ShouldBeConfiguredWithNoConfigurationHistory() {
		var simulator = new Simulator(1);
		var tokenSource = new CancellationTokenSource();
		var schedule = Schedule.EveryDay;
		var singleNode = ClusterFixtures.GenerateSingleNode();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
			Command.Configure(schedule),
		]);

		await simulator.Run(tokenSource.Token);

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ConfigurationUpdated>((@event, snapshot) => {
				Assert.False(snapshot.PrevState.Configuration.IsConfigured);
				Assert.True(snapshot.NewState.Configuration.IsConfigured);
				Assert.Equal(@event.Schedule, schedule);
				Assert.Equal(@event.UpdatedAt, snapshot.Time);
				Assert.Equal(@event.UpdatedAt, snapshot.NewState.Configuration.StartingPoint);
				Assert.Equal(@event.Schedule, snapshot.NewState.Configuration.Schedule);
			})
		]);
	}

	/// We check that when a user updates the auto scavenge process with a pre-existing configuration, that the state
	/// machine is updated accordingly and the configuration persisted.
	[Fact]
	public async Task ShouldOverrideExistingConfiguration() {
		var simulator = new Simulator(1);
		var tokenSource = new CancellationTokenSource();
		var previousSchedule = Schedule.EveryDay;
		var newSchedule = Schedule.EveryWeek;
		var previousUpdateTime = simulator.Now;
		var singleNode = ClusterFixtures.GenerateSingleNode();

		simulator.AddEventsToStorage(new Events.ConfigurationUpdated {
			Schedule = previousSchedule,
			UpdatedAt = previousUpdateTime,
		});

		var taskSource = new TaskCompletionSource<Response<Unit>>();

		simulator.EnqueueCommands([
			Command.ReceiveGossip(singleNode.InstanceId, [singleNode]),
		]);

		await simulator.Run(tokenSource.Token).WaitAsync(TimeSpan.FromSeconds(2));

		// At this point the simulator has been initialized with the previous configuration.
		simulator.EnqueueCommands([
			Command.FastForward(TimeSpan.FromHours(2)),
			Command.Configure(newSchedule, taskSource.SetResult),
		]);

		await simulator.Run(tokenSource.Token).WaitAsync(TimeSpan.FromSeconds(2));

		simulator.AssertSnapshots([
			Matches.MatchEvent<Events.ConfigurationUpdated>((@event, snapshot) => {
				Assert.True(snapshot.PrevState.Configuration.IsConfigured);
				Assert.Equal(snapshot.PrevState.Configuration.Schedule?.ToString(), previousSchedule.ToString());
				Assert.Equal(snapshot.PrevState.Configuration.StartingPoint, previousUpdateTime);
				Assert.True(snapshot.NewState.Configuration.IsConfigured);
				Assert.Equal(@event.Schedule.ToString(), newSchedule.ToString());
				Assert.Equal(@event.UpdatedAt, snapshot.Time);
				Assert.Equal(@event.UpdatedAt, snapshot.NewState.Configuration.StartingPoint);
				Assert.Equal(@event.Schedule.ToString(), snapshot.NewState.Configuration.Schedule?.ToString());
			})
		]);

		var resp = await taskSource.Task.WaitAsync(TimeSpan.FromSeconds(2));
		Assert.True(resp.IsSuccessful(out _));
	}
}
