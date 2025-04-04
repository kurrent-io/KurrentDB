// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Messages;

namespace EventStore.Core.Metrics;

public interface IPersistentSubscriptionTracker {
	public static IPersistentSubscriptionTracker NoOp => NoOpTracker.Instance;

	void OnNewStats(IReadOnlyList<MonitoringMessage.PersistentSubscriptionInfo> newStats);
}

file class NoOpTracker : IPersistentSubscriptionTracker {
	public static NoOpTracker Instance { get; } = new();
	public void OnNewStats(IReadOnlyList<MonitoringMessage.PersistentSubscriptionInfo> newStats) { }
}
