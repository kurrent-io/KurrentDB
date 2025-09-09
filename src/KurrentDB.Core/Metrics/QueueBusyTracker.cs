// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

namespace KurrentDB.Core.Metrics;

public interface IQueueBusyTracker {
	void EnterBusy();
	void EnterIdle();

	public static IQueueBusyTracker NoOp { get; } = new NoOpQueueBusyTracker();
}

public class QueueBusyTracker : IQueueBusyTracker {
	private readonly Stopwatch _stopwatch = new();

	public QueueBusyTracker(AverageMetric metric, string label) {
		metric.Register(label, () => _stopwatch.Elapsed.TotalSeconds);
	}

	public void EnterBusy() => _stopwatch.Start();

	public void EnterIdle() => _stopwatch.Stop();
}

file sealed class NoOpQueueBusyTracker : IQueueBusyTracker {
	void IQueueBusyTracker.EnterBusy() {
	}

	void IQueueBusyTracker.EnterIdle() {
	}
}
