// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using NCrontab;
using Serilog;

namespace EventStore.AutoScavenge.Domain;

/// <summary>
/// Auto scavenge process transient state.
/// </summary>
public class AutoScavengeState {
	private static readonly ILogger Log = Serilog.Log.ForContext<AutoScavengeState>();

	/// <summary>
	/// Nodes that have been scavenged in the current process cycle.
	/// </summary>
	private HashSet<NodeId> _scavengedNodes = [];

	/// <summary>
	/// Id of the current cluster scavenge, if any.
	/// </summary>
	// todo: only used by this and tests, make private
	public Guid ClusterScavengeId { get; private set; } = Guid.Empty;

	/// <summary>
	/// Id of the current node scavenge, if any.
	/// </summary>
	// todo: only used by this and tests, make private
	public Guid NodeScavengeId { get; private set; } = Guid.Empty;

	/// <summary>
	/// Current node that is designated for scavenging.
	/// </summary>
	// NodeScavengeId == Guid.Empty => a scavenge may have been started on the DesignatedNode
	// NodeScavengeId != Guid.Empty => a scavenge has been started on the DesignatedNode
	// todo: only used by this and tests, make private
	public NodeId? DesignatedNode { get; private set; } = null;

	/// <summary>
	/// Current list of cluster members.
	/// </summary>
	public HashSet<NodeId> ClusterMembers { get; private set; } = [];

	/// <summary>
	/// Last time a cluster scavenge was completed successfully or resume. Changing the anchor date when a cluster
	/// scavenge was resumed is for consistency’s sake. For example if a schedule is set for once every week on Sundays,
	/// if the process is paused for a whole week and resumed on the next Tuesday, the process will start on the next
	/// Sunday, instead of starting right away.
	/// </summary>
	public DateTime? LastClusterAnchorDateTime { get; private set; }

	/// <summary>
	/// Nodes remaining to be scavenged in the current cluster scavenge.
	/// </summary>
	public IEnumerable<NodeId> NodesToScavenge => ClusterMembers.Except(_scavengedNodes);

	public AutoScavengeConfiguration Configuration { get; init; } = new();

	/// <summary>
	/// Is there an ongoing auto scavenge process? By process, we mean if we have started a new cycle of node scavenges.
	/// </summary>
	public bool HasOngoingClusterScavenge(out Guid clusterScavengeId) {
		clusterScavengeId = ClusterScavengeId;
		return clusterScavengeId != Guid.Empty;
	}

	public bool HasSchedule(
		[MaybeNullWhen(returnValue: false)] out CrontabSchedule schedule,
		out DateTime nextOccurence) {

		var since = LastClusterAnchorDateTime ?? Configuration.StartingPoint;

		if (since is null || Configuration.Schedule is null) {
			schedule = default;
			nextOccurence = default;
			return false;
		}

		schedule = Configuration.Schedule;
		nextOccurence = schedule.GetNextOccurrence(since.Value);
		return true;
	}

	public T Visit<T>(
		Func<T> onNotConfigured,
		Func<Guid, T> onContinuingClusterScavenge,
		Func<NodeId, Guid, T> onMonitoringNodeScavenge,
		Func<NodeId?, Guid?, T> onPausing,
		Func<T> onPausedWhileIdle,
		Func<T> onPausedWhileScavengingCluster,
		Func<NodeId, T> onStartingNodeScavenge,
		Func<CrontabSchedule, DateTime, T> onWaiting) {
		switch (Status) {
			case AutoScavengeStatus.NotConfigured:
				return onNotConfigured();

			case AutoScavengeStatus.ContinuingClusterScavenge:
				return onContinuingClusterScavenge(ClusterScavengeId);

			case AutoScavengeStatus.MonitoringNodeScavenge:
				return onMonitoringNodeScavenge(DesignatedNode!.Value, NodeScavengeId);

			case AutoScavengeStatus.Pausing:
				return onPausing(DesignatedNode, NodeScavengeId == Guid.Empty ? null : NodeScavengeId);

			case AutoScavengeStatus.PausedWhileIdle:
				return onPausedWhileIdle();

			case AutoScavengeStatus.PausedWhileScavengingCluster:
				return onPausedWhileScavengingCluster();

			case AutoScavengeStatus.StartingNodeScavenge:
				return onStartingNodeScavenge(DesignatedNode!.Value);

			case AutoScavengeStatus.Waiting:
				if (!HasSchedule(out var schedule, out var nextOccurence))
					throw new Exception("Invalid state");
				return onWaiting(schedule, nextOccurence);

			default: throw new Exception("Unexpected Status");
		}
	}

	/// <summary>
	/// Auto scavenge process status
	/// </summary>
	/// Checks that the invariants are maintained when changing status
	AutoScavengeStatus _status = AutoScavengeStatus.NotConfigured;
	public AutoScavengeStatus Status {
		get {
			return _status;
		}
		private set {
			switch (value) {
				// in these states we do not have an ongoing cluster scavenge
				case AutoScavengeStatus.NotConfigured:
				case AutoScavengeStatus.Waiting:
				case AutoScavengeStatus.PausedWhileIdle:
					if (HasOngoingClusterScavenge(out _))
						throw new InvalidOperationException($"Cannot transition to {value}. There is an ongoing cluster scavenge.");
					break;

				// in these states we do have an ongoing cluster scavenge
				case AutoScavengeStatus.ContinuingClusterScavenge:
				case AutoScavengeStatus.PausedWhileScavengingCluster:
				case AutoScavengeStatus.Pausing:
					if (!HasOngoingClusterScavenge(out _))
						throw new InvalidOperationException($"Cannot transition to {value}. There is no ongoing cluster scavenge.");
					break;

				// in these states it's possible that a node scavenge is running
				case AutoScavengeStatus.StartingNodeScavenge:
					if (!HasOngoingClusterScavenge(out _))
						throw new InvalidOperationException($"Cannot transition to {value}. There is no ongoing cluster scavenge.");
					if (DesignatedNode is null)
						throw new InvalidOperationException($"Cannot transition to {value}. There is no designated node.");
					break;

				// in these states we have an ongoing node scavenge
				case AutoScavengeStatus.MonitoringNodeScavenge:
					if (!HasOngoingClusterScavenge(out _))
						throw new InvalidOperationException($"Cannot transition to {value}. There is no ongoing cluster scavenge.");
					if (DesignatedNode is null || NodeScavengeId == Guid.Empty)
						throw new InvalidOperationException($"Cannot transition to {value}. There is no ongoing node scavenge.");
					break;
				default:
					throw new InvalidOperationException($"Cannot transition to {value}. It is an unknown state.");
			}
			_status = value;
		}
	}

	/// <summary>
	/// Deep instance copy, useful for testing purposes
	/// </summary>
	public AutoScavengeState Clone() {
		return new AutoScavengeState {
			ClusterScavengeId = ClusterScavengeId,
			ClusterMembers = ClusterMembers.ToHashSet(),
			DesignatedNode = DesignatedNode,
			LastClusterAnchorDateTime = LastClusterAnchorDateTime,
			_scavengedNodes = _scavengedNodes.ToHashSet(),
			NodeScavengeId = NodeScavengeId,
			Configuration = Configuration.Clone(),
			Status = Status,
		};
	}

	/// <summary>
	/// Resets internal state to default values.
	/// </summary>
	public void Clear() {
		ClusterScavengeId = Guid.Empty;
		DesignatedNode = null;
		_scavengedNodes.Clear();
		ClusterMembers.Clear();
		Configuration.Clear();
		LastClusterAnchorDateTime = null;
		Status = AutoScavengeStatus.NotConfigured;
	}

	/// <summary>
	/// Apply an event to the current state.
	/// </summary>
	/// <param name="event"></param>
	public void Apply(IEvent @event) {
		switch (@event) {
			case Events.ClusterScavengeStarted msg:
				Handle(msg);
				break;
			case Events.ClusterScavengeCompleted msg:
				Handle(msg);
				break;
			case Events.NodeScavengeStarted msg:
				Handle(msg);
				break;
			case Events.NodeScavengeCompleted msg:
				Handle(msg);
				break;
			case Events.NodeDesignated msg:
				Handle(msg);
				break;
			case Events.ClusterMembersChanged msg:
				Handle(msg);
				break;
			case Events.Initialized msg:
				Handle(msg);
				break;
			case Events.PauseRequested msg:
				Handle(msg);
				break;
			case Events.Paused msg:
				Handle(msg);
				break;
			case Events.Resumed msg:
				Handle(msg);
				break;
			case Events.ConfigurationUpdated msg:
				Handle(msg);
				break;
		}
	}

	private void Handle(Events.ClusterScavengeStarted msg) {
		ClusterScavengeId = msg.ClusterScavengeId;
		_scavengedNodes.Clear();
		ClusterMembers = msg.Members.ToHashSet();
		Status = AutoScavengeStatus.ContinuingClusterScavenge;
	}

	private void Handle(Events.ClusterScavengeCompleted msg) {
		if (ClusterScavengeId != msg.ClusterScavengeId) {
			Log.Warning("Ignoring uncorrelated {EventType} event", msg.Type);
			return;
		}

		ClusterMembers = msg.Members.ToHashSet();
		LastClusterAnchorDateTime = msg.CompletedAt;
		ClusterScavengeId = Guid.Empty;
		Status = AutoScavengeStatus.Waiting;
	}

	private void Handle(Events.NodeScavengeStarted msg) {
		if (DesignatedNode != msg.NodeId) {
			Log.Warning("Found out of order {EventType} event. Skipping it", msg.Type);
			return;
		}

		NodeScavengeId = msg.ScavengeId;
		Status = AutoScavengeStatus.MonitoringNodeScavenge;
	}

	private void Handle(Events.NodeScavengeCompleted msg) {
		if (DesignatedNode != msg.NodeId) {
			Log.Warning("Found out of order {EventType} event. Skipping it", msg.Type);
			return;
		}

		DesignatedNode = null;
		NodeScavengeId = Guid.Empty;
		_scavengedNodes.Add(msg.NodeId);
		Status = AutoScavengeStatus.ContinuingClusterScavenge;
	}

	private void Handle(Events.NodeDesignated msg) {
		DesignatedNode = msg.NodeId;
		Status = AutoScavengeStatus.StartingNodeScavenge;
	}

	private void Handle(Events.ClusterMembersChanged msg) {
		foreach (var nodeId in msg.Added)
			ClusterMembers.Add(nodeId);

		foreach (var nodeId in msg.Removed) {
			ClusterMembers.Remove(nodeId);
			_scavengedNodes.Remove(nodeId);

			if (DesignatedNode == nodeId) {
				Log.Information("Node {NodeId} was designated to scavenge but has been removed from the cluster", nodeId);
				DesignatedNode = null;
				NodeScavengeId = Guid.Empty;

				if (Status != AutoScavengeStatus.Pausing) {
					Status = AutoScavengeStatus.ContinuingClusterScavenge;
				} else {
					// we are pausing but the designated node has left the cluster
					// we remain in the pausing state, the manager will transition us to paused.
				}
			}
		}
	}

	private void Handle(Events.Initialized msg) {
		if (Configuration.IsConfigured)
			LastClusterAnchorDateTime = msg.InitializedAt;
	}

	private void Handle(Events.PauseRequested msg) {
		Status = AutoScavengeStatus.Pausing;
	}

	private void Handle(Events.Paused msg) {
		DesignatedNode = null;

		Status = ClusterScavengeId == Guid.Empty
			? AutoScavengeStatus.PausedWhileIdle
			: AutoScavengeStatus.PausedWhileScavengingCluster;
	}

	private void Handle(Events.Resumed msg) {
		Status =
			ClusterScavengeId != Guid.Empty ? AutoScavengeStatus.ContinuingClusterScavenge :
			Configuration.IsConfigured ? AutoScavengeStatus.Waiting :
			AutoScavengeStatus.NotConfigured;

		if (Configuration.IsConfigured)
			LastClusterAnchorDateTime = msg.ResumedAt;
	}

	private void Handle(Events.ConfigurationUpdated msg) {
		Configuration.Apply(msg);
		LastClusterAnchorDateTime = msg.UpdatedAt;

		if (Status == AutoScavengeStatus.NotConfigured)
			Status = AutoScavengeStatus.Waiting;
	}
}
