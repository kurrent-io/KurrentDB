// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Linq;
using EventStore.Core.Services.Monitoring.Stats;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Bus;

public class QueueMonitor {
	private static readonly ILogger Log = Serilog.Log.ForContext<QueueMonitor>();
	public static readonly QueueMonitor Default = new QueueMonitor();

	private readonly ConcurrentDictionary<IMonitoredQueue, IMonitoredQueue> _queues =
		new ConcurrentDictionary<IMonitoredQueue, IMonitoredQueue>();

	private QueueMonitor() {
	}

	public void Register(IMonitoredQueue monitoredQueue) {
		_queues[monitoredQueue] = monitoredQueue;
	}

	public void Unregister(IMonitoredQueue monitoredQueue) {
		IMonitoredQueue v;
		_queues.TryRemove(monitoredQueue, out v);
	}

	public QueueStats[] GetStats() {
		var stats = _queues.Keys.OrderBy(x => x.Name).Select(queue => queue.GetStatistics()).ToArray();
		return stats;
	}
}
