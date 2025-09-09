// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Monitoring.Stats;

namespace KurrentDB.Core.Bus;

partial class ThreadPoolMessageScheduler : IMonitoredQueue {
	private readonly QueueTracker _tracker;
	private readonly IQueueStatsCollector _statsCollector;

	// set the property only if SynchronizeMessagesWithUnknownAffinity is set to 'true'
	public QueueTrackers Trackers {
		init => _tracker = value.GetTrackerForQueue(Name);
	}

	// set the property only if SynchronizeMessagesWithUnknownAffinity is set to 'true'
	public QueueStatsManager StatsManager {
		init => _statsCollector = value.CreateQueueStatsCollector(Name);
	}

	private static QueueMonitor Monitor => QueueMonitor.Default;

	QueueStats IMonitoredQueue.GetStatistics()
		=> _statsCollector.GetStatistics(int.CreateSaturating(_processingCount));
}
