// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using EventStore.AutoScavenge.Domain;
using NCrontab;

namespace EventStore.AutoScavenge;

public class Events {
	public class ConfigurationUpdated : IEvent {
		public required CrontabSchedule Schedule { get; init; }
		public required DateTime UpdatedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.ConfigurationUpdated;
	}

	public class ClusterMembersChanged : IEvent {
		public required List<NodeId> Added { get; init; }
		public required List<NodeId> Removed { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.ClusterMembersChanged;
	}

	public class ClusterScavengeStarted : IEvent {
		public required Guid ClusterScavengeId { get; init; }
		public required List<NodeId> Members { get; init; }
		public required DateTime StartedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.ClusterScavengeStarted;
	}

	public class ClusterScavengeCompleted : IEvent {
		public required Guid ClusterScavengeId { get; init; }
		public required List<NodeId> Members { get; init; }
		public required DateTime CompletedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.ClusterScavengeCompleted;
	}

	public class NodeDesignated : IEvent {
		public required NodeId NodeId { get; init; }
		public required DateTime DesignatedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.NodeDesignated;
	}

	public class NodeScavengeStarted : IEvent {
		public required NodeId NodeId { get; init; }
		public required Guid ScavengeId { get; init; }
		public required DateTime StartedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.NodeScavengeStarted;
	}

	public class NodeScavengeCompleted : IEvent {
		public required NodeId NodeId { get; init; }
		public required DateTime CompletedAt { get; init; }
		public required string Result { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.NodeScavengeCompleted;
	}

	public class Initialized : IEvent {
		public required DateTime InitializedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.Initialized;
	}

	public class PauseRequested : IEvent {
		public required DateTime RequestedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.PauseRequested;
	}

	public class Paused : IEvent {
		public required DateTime PausedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.Paused;
	}

	public class Resumed : IEvent {
		public required DateTime ResumedAt { get; init; }

		[JsonIgnore]
		public string Type => EventTypes.Resumed;
	}
}
