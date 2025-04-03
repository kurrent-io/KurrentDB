// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Messaging;

// The name of this enum and its members are used for metrics
public enum CoreMessage {
	None,
	Authentication,
	Awake,
	Client,
	ClusterClient,
	Election,
	Gossip,
	Grpc,
	Http,
	IODispatcher,
	LeaderDiscovery,
	Monitoring,
	Misc,
	Redaction,
	Replication,
	ReplicationTracking,
	Storage,
	Subscription,
	System,
	Tcp,
	Telemetry,
	Timer,
	UserManagement
}
