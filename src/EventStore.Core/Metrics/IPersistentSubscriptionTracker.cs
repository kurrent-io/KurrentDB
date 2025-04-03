// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
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
