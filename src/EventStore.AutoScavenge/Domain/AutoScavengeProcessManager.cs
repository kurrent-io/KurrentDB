// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Scavengers;
using EventStore.AutoScavenge.Sources;
using EventStore.AutoScavenge.TimeProviders;
using EventStore.POC.IO.Core;
using NCrontab;
using Serilog.Events;

namespace EventStore.AutoScavenge.Domain;

/// <summary>
/// Responsible for managing the state transitions and operations related to the auto scavenge process in an Event Store
/// cluster.
/// </summary>
/// <param name="timeProvider">
/// Used when dealing with time-sensitive operations like when should the next auto scavenge process start.
/// </param>
/// <param name="source">
/// Accesses the configuration and events related to the auto scavenge process.
/// </param>
public class AutoScavengeProcessManager {
	private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<AutoScavengeProcessManager>();

	readonly ITimeProvider _timeProvider;
	readonly ISource _source;
	readonly IOperationsClient _operationClient;
	readonly INodeScavenger _scavenger;
	readonly int _clusterSize;

	readonly Queue<IEvent> _uncommittedEvents = [];

	NodeId _nodeId = new() { Address = "", Port = 0 };

	NodeState _nodeState = NodeState.NotLeader;

	List<ClusterMember> _gossip = [];
	bool _isHealthyCluster;

	// EventSourced state
	public AutoScavengeState State { get; } = new();

	public AutoScavengeProcessManager(
		int clusterSize,
		ITimeProvider timeProvider,
		ISource source,
		IOperationsClient operationsClient,
		INodeScavenger scavenger) {

		_timeProvider = timeProvider;
		_source = source;
		_operationClient = operationsClient;
		_scavenger = scavenger;
		_clusterSize = clusterSize;
	}

	/// <summary>
	/// This is the entry point for the state machine. It operates one step at a time, requiring a command to progress.
	/// </summary>
	/// <remarks>
	/// The state machine can generate another command, an event, or both. Here's the distinction:
	/// - A command instructs the state machine to perform an action.
	/// - An event notifies that a decision has been made.
	///
	/// It's expected that an event is persisted and should be retrievable using <see cref="ISource" />.
	/// Only an event can modify the transient state of the state machine, which is done when <see cref="Apply"/> is called.
	/// </remarks>
	/// <returns>Events to persist and optionally a command to process afterward</returns>
	public async ValueTask<Transition> RunAsync(ICommand command, CancellationToken token) {
		switch (command) {
			case Commands.ReceiveGossip gossip:
				await ReceiveGossipAsync(gossip.Msg, token);
				break;
			case Commands.Configure configure:
				configure.SetConditionalResponse(Configure(configure));
				break;
			case Commands.GetStatus status:
				status.SetConditionalResponse(GetStatus());
				break;
			case Commands.PauseProcess pause:
				pause.SetConditionalResponse(PauseProcess());
				break;
			case Commands.ResumeProcess resume:
				resume.SetConditionalResponse(ResumeProcess());
				break;
			case Commands.Assess:
				await AssessAsync(token);
				break;
			default:
				return Transition.Noop;
		}

		// if we produced any events, commit them and reassess immediately
		if (_uncommittedEvents.Count > 0) {
			var events = _uncommittedEvents.ToArray();
			_uncommittedEvents.Clear();
			return new Transition(events, Commands.Assess.Instance);
		}

		if (command is Commands.Assess)
			return Transition.Noop;

		return new Transition([], Commands.Assess.Instance);
	}

	/// <summary>
	/// Updates the transient state of the state machine.
	/// </summary>
	public void Apply(IEvent @event) {
		State.Apply(@event);
	}

	/// <summary>
	/// Resets the transient state of the state machine.
	/// </summary>
	private void Clear() {
		State.Clear();
		_uncommittedEvents.Clear();
	}

	// Cluster is healthy if all cluster nodes (not readonly replicas) are present
	// todo: we may want to consider the statuses and the checkpoints
	private bool IsHealthyCluster() {
		var clusterMembers = 0;

		foreach (var member in _gossip) {
			if (member.InstanceId != Guid.Empty && !member.IsReadOnlyReplica)
				clusterMembers++;
		}

		return clusterMembers == _clusterSize;
	}

	/// <summary>
	/// Handles a gossip message from the cluster.
	/// </summary>
	/// <remarks>
	/// This command serves as a central point in the state machine. It is triggered by frequent gossip messages,
	/// which also act as a trigger mechanism to track the progress of operations such as the start of a new auto
	/// scavenge process, completion or retry of a node scavenge. This command also detects changes in the cluster
	/// topology.
	///
	/// It also tracks the node state this way. If it is no longer leader it resets all its transient state.
	/// </remarks>
	private async ValueTask ReceiveGossipAsync(GossipMessage msg, CancellationToken token) {
		_gossip = msg.Members;
		_isHealthyCluster = IsHealthyCluster();

		var previousStatus = _nodeState;

		foreach (var member in _gossip) {
			if (member.InstanceId != msg.NodeId)
				continue;

			// member is this node
			_nodeId = member.ToNodeId();

			_nodeState = member.State == "Leader"
				? NodeState.Leader
				: NodeState.NotLeader;
		}

		if (_nodeState == NodeState.Leader) {
			if (previousStatus != NodeState.Leader) {
				// became the leader
				await LoadConfigurationAsync(token);
				await LoadStateAsync(token);
			}
		} else {
			if (previousStatus == NodeState.Leader) {
				// no longer the leader
				Clear();
			}
			return;
		}

		// we are the leader
		if (!_isHealthyCluster)
			return;

		if (!State.HasOngoingClusterScavenge(out _))
			return;

		// A cluster scavenge is in progress (even if paused), it may be affected by the new gossip.
		var gossipNodeIds = _gossip.Select(x => x.ToNodeId()).ToList();
		var newMembers = gossipNodeIds.Except(State.ClusterMembers).ToList();
		var removedMembers = State.ClusterMembers.Except(gossipNodeIds).ToList();

		if (newMembers.Count == 0 && removedMembers.Count == 0)
			return;

		_uncommittedEvents.Enqueue(new Events.ClusterMembersChanged {
			Added = newMembers,
			Removed = removedMembers,
		});
	}

	/// <summary>
	/// Handles auto scavenge configuration request.
	/// </summary>
	/// <remarks>
	/// Because we always take the latest configuration, we don't need to reload passed configuration events. Updating
	/// an auto scavenge process configuration must have no impact on an ongoing auto scavenge process.
	/// </remarks>
	private Response<Unit> Configure(Commands.Configure command) {
		if (_nodeState != NodeState.Leader)
			return Response.Rejected<Unit>("Node is not the leader");

		_uncommittedEvents.Enqueue(new Events.ConfigurationUpdated {
			Schedule = command.Schedule,
			UpdatedAt = _timeProvider.Now,
		});

		return Response.Successful();
	}

	private async ValueTask LoadConfigurationAsync(CancellationToken token) {
		Log.Information("Loading auto scavenge configuration...");

		var @event = await _source.ReadConfigurationEvent(token);

		if (@event != null)
			Apply(@event);

		Log.Information("Loaded auto scavenge configuration");
	}

	private async ValueTask LoadStateAsync(CancellationToken token) {
		Log.Information("Loading auto scavenge state...");

		await foreach (var @event in _source.ReadAutoScavengeEvents(token))
			Apply(@event);

		Apply(new Events.Initialized {
			InitializedAt = _timeProvider.Now
		});

		Log.Information("Loaded auto scavenge state");
	}

	/// <summary>
	///  Handles auto scavenge process assessment.
	/// </summary>
	/// <remarks>
	/// During assessment, the state machine checks holistically the auto scavenge process current state:
	/// <list type="bullet">
	/// <item>
	/// If enough time has passed since the last auto scavenge process, the state machine will start a new one.
	/// </item>
	/// <item>
	/// If a node has been designated for scavenge, the state machine will issue a remote scavenge request to that
	/// node.
	/// </item>
	/// <item>
	/// If a node is scavenging, the state machine checks the status of that scavenge.
	/// </item>
	/// </list>
	/// </remarks>
	private async ValueTask AssessAsync(CancellationToken token) {
		if (_nodeState != NodeState.Leader)
			return;

		// todo: consider reducing allocations here by passing a visitor object (nested struct? be careful not to box)
		// this is called once every ~5s, so not often, and the allocations will usually be gen1
		await State.Visit(
			onNotConfigured: DoNothingAsync,
			onPausedWhileIdle: DoNothingAsync,
			onPausedWhileScavengingCluster: DoNothingAsync,
			onPausing: TryToPause,
			onContinuingClusterScavenge: PickANode,
			onStartingNodeScavenge: StartNodeScavenge,
			onMonitoringNodeScavenge: CheckOnTheNodeScavenge,
			onWaiting: CheckTheSchedule);

		static ValueTask DoNothingAsync() => ValueTask.CompletedTask;

		async ValueTask TryToPause(NodeId? designatedNode, Guid? nodeScavengeId) {
			if (designatedNode is null) {
				_uncommittedEvents.Enqueue(new Events.Paused {
					PausedAt = _timeProvider.Now,
				});
				return;
			}

			var success = await _scavenger.TryPauseScavengeAsync(
				designatedNode.Value.Address,
				designatedNode.Value.Port,
				nodeScavengeId, token);

			// If pausing the node scavenged failed, it will be retried on the next gossip message we got from
			// the cluster.
			if (!success)
				return;

			_uncommittedEvents.Enqueue(new Events.Paused {
				PausedAt = _timeProvider.Now,
			});
		}

		async ValueTask CheckOnTheNodeScavenge(NodeId designatedNode, Guid nodeScavengeId) {
			ScavengeStatus status;

			status = await _scavenger.TryGetScavengeAsync(
				designatedNode.Address,
				designatedNode.Port,
				nodeScavengeId,
				token);

			if (status is ScavengeStatus.Unknown) {
				Log.Information("Node scavenge on {NodeId} has Unknown status. Will retry.", designatedNode);
				// retry will be triggered by next gossip
				return;
			}

			if (status is ScavengeStatus.InProgress) {
				Log.Verbose("Node Scavenge on {NodeId} is still in progress", designatedNode);
				return;
			}

			if (status is ScavengeStatus.NotRunningUnknown) {
				Log.Information("Node scavenge on {NodeId} is not running for an unknown reason. Restarting it.",
					designatedNode);

				_uncommittedEvents.Enqueue(new Events.NodeDesignated {
					NodeId = designatedNode,
					DesignatedAt = _timeProvider.Now,
				});

				return;
			}

			Log.Write(
				status == ScavengeStatus.Success
					? LogEventLevel.Information
					: LogEventLevel.Warning,
				"Node scavenge on {NodeId} has status: {Status}. Marking it as completed",
				designatedNode, status);

			_uncommittedEvents.Enqueue(new Events.NodeScavengeCompleted {
				NodeId = designatedNode,
				CompletedAt = _timeProvider.Now,
				Result = status.ToString()
			});
		}

		async ValueTask StartNodeScavenge(NodeId designatedNode) {
			if (designatedNode == _nodeId && _clusterSize > 1) {
				Log.Information(
					"The leader {NodeId} is the last node to scavenge. We are resigning from leadership",
					_nodeId);

				_operationClient.Resign();
				return;
			}

			var scavengeId = await _scavenger.TryStartScavengeAsync(designatedNode.Address, designatedNode.Port, token);

			if (scavengeId == null)
				// The idea is to let another gossip message come in and trigger the assessment again.
				return;

			_uncommittedEvents.Enqueue(new Events.NodeScavengeStarted {
				NodeId = designatedNode,
				ScavengeId = scavengeId.Value,
				StartedAt = _timeProvider.Now,
			});
		}

		ValueTask PickANode(Guid clusterScavengeId) {
			DetermineBestCandidate(clusterScavengeId);
			return ValueTask.CompletedTask;
		}

		ValueTask CheckTheSchedule(CrontabSchedule schedule, DateTime nextOccurence) {
			// We are active but not currently scavenging the cluster. Maybe it's time to start!
			if (_timeProvider.Now < nextOccurence)
				return ValueTask.CompletedTask;

			if (!_isHealthyCluster) {
				Log.Information("Autoscavenge is due. Waiting for a healthy cluster");
				return ValueTask.CompletedTask;
			}

			// We are due for a new auto scavenge process.
			var activeMembers = _gossip.Select(x => x.ToNodeId());

			_uncommittedEvents.Enqueue(new Events.ClusterScavengeStarted {
				ClusterScavengeId = Guid.NewGuid(),
				Members = activeMembers.ToList(),
				StartedAt = _timeProvider.Now,
			});

			return ValueTask.CompletedTask;
		}
	}

	/// <summary>
	/// Determines the next best candidate for the auto scavenge process.
	/// </summary>
	/// <remarks>
	/// This stage also handles the completion of the auto scavenge process when there are no more nodes to scavenge.
	/// </remarks>
	private void DetermineBestCandidate(Guid clusterScavengeId) {
		if (!_isHealthyCluster) {
			Log.Information("Waiting for a healthy cluster before picking a node to scavenge");
			return;
		}

		var targets = State.NodesToScavenge.ToList();

		// We detect if there is no longer any node to scavenge left.
		if (targets.Count == 0) {
			_uncommittedEvents.Enqueue(new Events.ClusterScavengeCompleted {
				ClusterScavengeId = clusterScavengeId,
				Members = State.ClusterMembers.ToList(),
				CompletedAt = _timeProvider.Now,
			});

			Log.Information("Auto scavenge process {ClusterScavengeId} completed", clusterScavengeId);
			return;
		}

		var candidate = targets[0];
		var minWriterCheckpoint = long.MaxValue;

		// We choose the node that is the farthest behind in its replication process.
		foreach (var target in targets) {
			var targetWriterCheckpoint = _gossip.Find(m => m.ToNodeId() == target)!.WriterCheckpoint;

			if (targetWriterCheckpoint > minWriterCheckpoint)
				continue;

			if (target == _nodeId)
				continue;

			// In case a follower node is up-to-date, we will always choose it over the leader.
			if (targetWriterCheckpoint == minWriterCheckpoint && candidate == _nodeId) {
				candidate = target;
				continue;
			}

			minWriterCheckpoint = targetWriterCheckpoint;
			candidate = target;
		}

		Log.Information("Node {NodeId} is the next best candidate for scavenge", candidate);
		_uncommittedEvents.Enqueue(new Events.NodeDesignated {
			NodeId = candidate,
			DesignatedAt = _timeProvider.Now,
		});
	}

	/// <summary>
	/// Pauses the auto scavenge process.
	/// </summary>
	private Response<Unit> PauseProcess() {
		if (_nodeState != NodeState.Leader)
			return Response.Rejected<Unit>("Node is not the leader");

		switch (State.Status) {
			case AutoScavengeStatus.PausedWhileIdle:
			case AutoScavengeStatus.PausedWhileScavengingCluster:
				return Response.Successful();

			case AutoScavengeStatus.Pausing:
				return Response.Accepted();

			case AutoScavengeStatus.NotConfigured:
			case AutoScavengeStatus.ContinuingClusterScavenge:
			case AutoScavengeStatus.Waiting:
				_uncommittedEvents.Enqueue(new Events.Paused {
					PausedAt = _timeProvider.Now
				});
				return Response.Successful();

			case AutoScavengeStatus.StartingNodeScavenge:
			case AutoScavengeStatus.MonitoringNodeScavenge:
				_uncommittedEvents.Enqueue(new Events.PauseRequested {
					RequestedAt = _timeProvider.Now
				});
				return Response.Accepted();
			default:
				throw new InvalidOperationException();
		}
	}

	/// <summary>
	/// Resumes the auto scavenge process.
	/// </summary>
	private Response<Unit> ResumeProcess() {
		if (_nodeState != NodeState.Leader)
			return Response.Rejected<Unit>("Node is not the leader");

		switch (State.Status) {
			case AutoScavengeStatus.PausedWhileIdle:
			case AutoScavengeStatus.PausedWhileScavengingCluster:
				_uncommittedEvents.Enqueue(new Events.Resumed {
					ResumedAt = _timeProvider.Now,
				});
				return Response.Successful();

			case AutoScavengeStatus.Pausing:
				return Response.Rejected("Auto scavenge process is currently pausing");

			case AutoScavengeStatus.NotConfigured:
			case AutoScavengeStatus.ContinuingClusterScavenge:
			case AutoScavengeStatus.StartingNodeScavenge:
			case AutoScavengeStatus.MonitoringNodeScavenge:
			case AutoScavengeStatus.Waiting:
				return Response.Successful();
			default:
				throw new InvalidOperationException();
		}
	}

	/// <summary>
	/// Produces the status breakdown of the auto scavenge process. If the auto scavenge process transient states
	/// are loaded from the persistent storage, We produce the breakdown on the spot. If the state machine hasn't
	/// loaded, we load it first then we produce the status breakdown.
	/// </summary>
	private Response<AutoScavengeStatusResponse> GetStatus() {
		if (_nodeState != NodeState.Leader)
			return Response.Rejected<AutoScavengeStatusResponse>("Node is not the leader");

		State.HasSchedule(out var schedule, out var nextOccurence);

		TimeSpan? nextCycle;

		if (State.HasOngoingClusterScavenge(out _)) {
			// cycle is happening right now
			nextCycle = TimeSpan.Zero;
		} else if (schedule is null) {
			// not configured, there is no next cycle
			nextCycle = null;
		} else if (State.Status is AutoScavengeStatus.PausedWhileIdle) {
			// we are paused while idle, show the time of the next cycle if we resumed now
			nextCycle = schedule.GetNextOccurrence(_timeProvider.Now) - _timeProvider.Now;
		} else {
			// waiting
			nextCycle = nextOccurence - _timeProvider.Now;
		}

		if (nextCycle < TimeSpan.Zero)
			nextCycle = TimeSpan.Zero;

		var state = State.Status switch {
			AutoScavengeStatus.NotConfigured =>
				AutoScavengeStatusResponse.Status.NotConfigured,

			AutoScavengeStatus.Waiting =>
				AutoScavengeStatusResponse.Status.Waiting,

			AutoScavengeStatus.ContinuingClusterScavenge or
			AutoScavengeStatus.MonitoringNodeScavenge or
			AutoScavengeStatus.StartingNodeScavenge =>
				AutoScavengeStatusResponse.Status.InProgress,

			AutoScavengeStatus.Pausing =>
				AutoScavengeStatusResponse.Status.Pausing,

			AutoScavengeStatus.PausedWhileIdle or
			AutoScavengeStatus.PausedWhileScavengingCluster =>
				AutoScavengeStatusResponse.Status.Paused,

			_ => throw new InvalidOperationException(),
		};

		return Response.Successful(new AutoScavengeStatusResponse(state, schedule, nextCycle));
	}
}
