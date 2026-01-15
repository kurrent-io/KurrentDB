// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Threading;
using DotNext.Diagnostics.Metrics;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Monitoring.Stats;

namespace KurrentDB.Core.Bus;

partial class ThreadPoolMessageScheduler : IMonitoredQueue {
	private readonly QueueTracker _tracker;
	private readonly IQueueStatsCollector _statsCollector;
	private MeterListener _queueLengthListener;
	private IQueueLengthObserver _queueLengthObserver;

	// set the property only if Strategy is not TreatUnknownAffinityAsNoAffinity
	public QueueTrackers Trackers {
		init => _tracker = value.GetTrackerForQueue(Name);
	}

	// set the property only if Strategy is not TreatUnknownAffinityAsNoAffinity
	public QueueStatsManager StatsManager {
		init => _statsCollector = value.CreateQueueStatsCollector(Name);
	}

	private static QueueMonitor Monitor => QueueMonitor.Default;

	QueueStats IMonitoredQueue.GetStatistics()
		=> _statsCollector.GetStatistics(_queueLengthObserver?.CaptureQueueLength() ?? int.CreateSaturating(_processingCount));

	internal interface IQueueLengthObserver {
		int CaptureQueueLength();
	}

	private sealed class QueueLengthObserver(MeasurementFilter<UpDownCounter<int>> filter, IQueueStatsCollector collector) : InstrumentObserver<int, UpDownCounter<int>>(filter), IQueueLengthObserver {
		// See QueuedSynchronizer.Metrics file
		private const string MeterName = "DotNext.Threading.AsyncLock";
		private const string QueuedSynchronizerSuspenderCallersCountInstrument = "suspended-callers-count";

		private int _queueLength;

		protected override void Record(int value)
			=> collector.ReportQueueLength(_queueLength += value);

		int IQueueLengthObserver.CaptureQueueLength() => _queueLength;

		public static bool IsQueueLengthCounter(UpDownCounter<int> counter)
			=> counter is { Name: QueuedSynchronizerSuspenderCallersCountInstrument, Meter.Name: MeterName };
	}
}
