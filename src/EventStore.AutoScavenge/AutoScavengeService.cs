// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Threading.Channels;
using EventStore.AutoScavenge.Clients;
using EventStore.AutoScavenge.Converters;
using EventStore.AutoScavenge.Domain;
using EventStore.AutoScavenge.Scavengers;
using EventStore.AutoScavenge.Sources;
using EventStore.POC.IO.Core;
using TimeProvider = EventStore.AutoScavenge.TimeProviders.TimeProvider;

namespace EventStore.AutoScavenge;

internal class AutoScavengeService : IDisposable {
	private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<AutoScavengeService>();

	private static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new EventJsonConverter(), new CrontableScheduleJsonConverter() }
	};

	private readonly CancellationTokenSource _cts;
	private readonly EventStoreOptions _options;
	private readonly IClient _client;
	private readonly IOperationsClient _operationsClient;
	private readonly Channel<ICommand> _commands;
	private readonly INodeScavenger _scavenger;
	private readonly IGossipAware _gossipHandler;

	public AutoScavengeService(
		EventStoreOptions options,
		IClient client,
		IOperationsClient operationsClient,
		Channel<ICommand> commands,
		INodeScavenger scavenger,
		IGossipAware gossipHandler,
		CancellationTokenSource cts) {

		_options = options;
		_client = client;
		_operationsClient = operationsClient;
		_commands = commands;
		_scavenger = scavenger;
		_gossipHandler = gossipHandler;
		_cts = cts;
	}

	public Task StartAsync() {
		// todo We might consider bulletproofing subscription tasks
		var sub2 = Task.Run(() => SubscribeToGossip(_cts.Token), _cts.Token).WarnOnCompletion("AUTO SCAVENGE GOSSIP MONITOR STOPPED");
		var process = Process(_cts.Token).WarnOnCompletion("AUTO SCAVENGE PROCESS MANAGER STOPPED");
		return Task.CompletedTask;
	}

	public void Dispose() {
		_cts.Cancel();
	}

	private async Task Process(CancellationToken token) {
		var source = new ClientSource(_client);
		var machine = new AutoScavengeProcessManager(_options.ClusterSize, new TimeProvider(), source, _operationsClient, _scavenger);

		await foreach (var commandFromChannel in _commands.Reader.ReadAllAsync(token)) {
			// if an additional command was yielded execute it now, do not queue it to the channel.
			// this allows us to make the channel bounded without risk of deadlock.
			var command = commandFromChannel;
			while (command is not null) {
				Log.Verbose("Autoscavenge processing command {Command}. Queue {Count}", command, _commands.Reader.Count);
				command = await ProcessCommand(command);
			}
		}

		async Task<ICommand?> ProcessCommand(ICommand command) {
			try {
				var transition = await machine.RunAsync(command, token);

				foreach (var @event in transition.Events) {
					if (@event is Events.ConfigurationUpdated)
						await _client.WriteAsyncRetry(
							StreamNames.AutoScavengeConfiguration,
							[Serialize(@event)],
							-2, // Any
							token);
					else
						source.AutoScavengeStreamExpectedRevision = await _client.WriteAsyncRetry(
							StreamNames.AutoScavenges,
							[Serialize(@event)],
							source.AutoScavengeStreamExpectedRevision,
							token);

					// We only update the machine's state if persisting the event was successful
					machine.Apply(@event);
				}

				// events if any are successfully saved, we can now send the response
				command.OnSavedEvents();

				return transition.NextCommand;

			} catch (ResponseException.NotLeader) {
				Log.Information(
					"Node is no longer a leader and stopped driving the automatic scavenge process");
				command.OnServerError("Not leader");
				return null;

			} catch (Exception ex) {
				Log.Error(ex, "Unexpected exception in auto-scavenge plugin");
				await _cts.CancelAsync();
				command.OnServerError(ex.Message);
				return null;
			}
		}

		if (_cts.Token.IsCancellationRequested) {
			while (_commands.Reader.TryRead(out var command)) {
				switch (command) {
					case Commands.PauseProcess cmd:
						cmd.Callback(Response.ServerError("auto-scavenge errored"));
						break;
					case Commands.ResumeProcess cmd:
						cmd.Callback(Response.ServerError("auto-scavenge errored"));
						break;
					case Commands.GetStatus cmd:
						cmd.Callback(Response.ServerError<AutoScavengeStatusResponse>("auto-scavenge errored"));
						break;
				}
			}
		}
	}

	private async Task SubscribeToGossip(CancellationToken cancellationToken) {
		GossipMessage? lastGossip = null;
		var singleNode = false;

		await foreach(var @event in _client.SubscribeToStream("$mem-gossip", cancellationToken)) {
			try {
				var msg = JsonSerializer.Deserialize<GossipMessage>(@event.Data.Span)!;
				_gossipHandler.ReceiveGossipMessage(msg);
				await _commands.Writer.WriteAsync(new Commands.ReceiveGossip(msg), cancellationToken);

				// If we are running in a single-node configuration, we will only receive one gossip message for
				// the entirety of the node's lifetime.
				if (msg.Members.Count != 1 || msg.Members[0].State != "Leader")
					continue;

				singleNode = true;
				lastGossip = msg;
				break;
			} catch (JsonException ex) {
				Log.Fatal(ex, "Failed to deserialize gossip message");
				// TODO - Do we really want to break here? What about restarting the subscription?
				break;
			}
		}

		// If we are running in a single-node configuration, we simulate the reception of a gossip message to the
		// auto scavenge state machine because gossip messages act as a metronome to the auto scavenge process.
		// We don't update the proxy client because we won't ever need to proxy request in single-node mode.
		if (singleNode) {
			while (!cancellationToken.IsCancellationRequested) {
				await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
				await _commands.Writer.WriteAsync(new Commands.ReceiveGossip(lastGossip!), cancellationToken);
			}
		}
	}

	private static EventToWrite Serialize(IEvent @event) {
		return new EventToWrite(
			Guid.NewGuid(),
			@event.Type,
			"application/json",
			JsonSerializer.SerializeToUtf8Bytes(@event, JsonSerializerOptions),
			Array.Empty<byte>());
	}
}
