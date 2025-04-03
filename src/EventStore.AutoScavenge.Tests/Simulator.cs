// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;
using EventStore.AutoScavenge.Tests.TimeProviders;

namespace EventStore.AutoScavenge.Tests;

/// <summary>
/// Utility object that helps auto scavenge state machine testing.
/// </summary>
public class Simulator {
	private readonly FakeSource _source = new();
	private readonly AutoScavengeProcessManager _machine;
	private readonly Queue<ICommand> _queue = new();
	private readonly List<ReactEvent> _reactsEvents = new();
	private readonly List<ReactCommand> _reactsCommands = new();

	/// <summary>
	/// Holds all events and state updates that happened during the simulation.
	/// </summary>
	private readonly List<Snapshot> _snapshots = [];

	/// <summary>
	/// Holds all state transitions that happened during the simulation.
	/// </summary>
	private readonly List<ICommand> _transitions = [];

	public DateTime Now => Time.Now;

	public FakeScavenger Scavenger { get; } = new();

	public FakeOperationsClient OperationsClient { get; } = new();

	public FakeTimeProvider Time { get; } = new();


	public Simulator(int clusterSize) {
		_machine = new AutoScavengeProcessManager(clusterSize, Time, _source, OperationsClient, Scavenger);
	}

	/// <summary>
	/// Feeds the simulator with a list of commands to be executed. The simulator handles commands in a FIFO order.
	/// </summary>
	/// <param name="inputs"></param>
	public void EnqueueCommands(params ICommand[] inputs) {
		foreach (var command in inputs)
			_queue.Enqueue(command);
	}

	/// <summary>
	/// Add events to the source of the simulator. Those events are not applied to the state machine but will be
	/// available for the state machine to consume if it decides to load them.
	/// </summary>
	/// <param name="events"></param>
	public void AddEventsToStorage(params IEvent[] events) {
		foreach (var @event in events) {
			if (@event is Events.ConfigurationUpdated update)
				_source.AddConfigurationEvent(update);
			else
				_source.AddScavengeEvent(@event);
		}
	}

	/// <summary>
	/// Defines a list of actions to be executed if a specific event is emitted.
	/// </summary>
	/// <param name="reacts"></param>
	public void Reacts(params ReactEvent[] reacts) {
		foreach (var react in reacts)
			_reactsEvents.Add(react);
	}

	/// <summary>
	/// Defines a list of actions to be executed if the state machine moves do a specific command.
	/// </summary>
	/// <param name="reacts"></param>
	public void Reacts(params ReactCommand[] reacts) {
		foreach (var react in reacts)
			_reactsCommands.Add(react);
	}

	/// <summary>
	/// Runs the simulation until the command queue is empty or a <see cref="Suspend" /> command is found.
	/// </summary>
	/// <param name="token"></param>
	public async Task Run(CancellationToken token) {
		while (_queue.TryDequeue(out var command)) {
			token.ThrowIfCancellationRequested();

			if (command is SimulatorCommands.FastForward advance)
				Time.FastForward(advance.Time);

			if (command is SimulatorCommands.Suspend)
				break;

			while (command is not null)
				command = await ProcessCommand(command);
		}

		async Task<ICommand?> ProcessCommand(ICommand command) {
			var transition = await _machine.RunAsync(command, token);

			// So far we only support reacting after a command is processed but we can add more as the need arises.
			foreach (var react in _reactsCommands)
				if (react.Type == command.GetType())
					react.Fun(command);

			if (transition.NextCommand != null) {
				_queue.Enqueue(transition.NextCommand);
				_transitions.Add(transition.NextCommand);
			}

			foreach (var @event in transition.Events) {
				// When an event is emitted, we clone the both manager and configuration transient states before and after
				// the event is applied. This allows us deeply state transition between snapshots.
				var prevState = _machine.State.Clone();
				_machine.Apply(@event);
				_snapshots.Add(new Snapshot(
					@event,
					prevState,
					_machine.State.Clone(),
					Time.Now));

				// We run event reacts after the event is applied to the state machine.
				foreach (var react in _reactsEvents)
					if (react.Type == @event.GetType())
						react.Fun(@event);
			}

			command.OnSavedEvents();
			return transition.NextCommand;
		}
	}

	/// <summary>
	/// Asserts that there are as many matches as we got snapshots. The order of each match should also have the same
	/// type as the emitted event of that time.  A snapshot is taken every time an event is emitted
	/// and applied to the state machine.
	/// </summary>
	/// <param name="matches"></param>
	public void AssertSnapshots(List<Match> matches) {
		Assert.Equal(matches.Count, _snapshots.Count);

		for (var i = 0; i < matches.Count; i++) {
			var (type, fun) = matches[i];
			var snapshot = _snapshots[i];

			Assert.IsType(type, snapshot.Event);

			fun(snapshot.Event, snapshot);
		}
	}

	public void ClearSnapshots() => _snapshots.Clear();
}

public record Match(Type Type, Action<IEvent, Snapshot> Fun);
public record ReactEvent(Type Type, Action<IEvent> Fun);
public record ReactCommand(Type Type, Action<ICommand> Fun);

public static class Matches {
	 public static Match MatchEvent<T>(Action<T, Snapshot> fun) =>
		 new(typeof(T), (e, s) => fun((T)e, s));

	 public static Match MatchEvent<T>() =>
		 new(typeof(T), (_, _) => {});
}

public static class Reacts {
	 public static ReactEvent OnEvent<T>(Action<T> fun) =>
		 new(typeof(T), e => fun((T)e));

	 public static ReactCommand After<T>(Action<T> fun) =>
		 new(typeof(T), e => fun((T)e));
}
