// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring.Stats;

namespace EventStore.Core.Services.Monitoring;

public class StatsCollectorEnvelope(StatsContainer statsContainer) : IEnvelope {
	public void ReplyWith<T>(T message) where T : Message {
		if (message is MonitoringMessage.InternalStatsRequestResponse msg)
			statsContainer.Add(msg.Stats);
	}
}
