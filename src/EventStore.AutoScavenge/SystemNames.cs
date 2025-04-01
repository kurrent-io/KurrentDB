// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge;

public static class StreamNames {
	public const string AutoScavenges = "$auto-scavenges";
	public const string AutoScavengeConfiguration = "$auto-scavenge-configuration";
}

public static class EventTypes {
	public const string ConfigurationUpdated = "$configuration-updated";
	public const string ClusterMembersChanged = "$cluster-members-changed";

	public const string ClusterScavengeStarted = "$cluster-scavenge-started";
	public const string ClusterScavengeCompleted = "$cluster-scavenge-completed";

	public const string NodeDesignated = "$node-designated";

	public const string NodeScavengeStarted = "$node-scavenge-started";
	public const string NodeScavengeCompleted = "$node-scavenge-completed";

	public const string Initialized = "$initialized";
	public const string PauseRequested = "$pause-requested";
	public const string Paused = "$paused";
	public const string ResumeRequested = "$resume-requested";
	public const string Resumed = "$resumed";
}
