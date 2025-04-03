// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Serilog;
using Conf = EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.Metrics;

// Some of the trackers are shared between all the queues that have the same label
// We cache those in _sharedTrackers.
// Other trackers each queue has its own instance.
public class QueueTrackers {
	private static readonly ILogger Log = Serilog.Log.ForContext<QueueTrackers>();

	private readonly Dictionary<string, SharedTrackers> _sharedTrackers = new();

	private readonly PrivateTrackers _noOpPrivate = new(
		new QueueBusyTracker.NoOp());

	private readonly SharedTrackers _noOpShared = new(
		"NoOp",
		new DurationMaxTracker.NoOp(),
		new QueueProcessingTracker.NoOp());

	private readonly Conf.LabelMappingCase[] _cases;
	private readonly Func<string, IQueueBusyTracker> _busyTrackerFactory;
	private readonly Func<string, IDurationMaxTracker> _durationTrackerFactory;
	private readonly Func<string, IQueueProcessingTracker> _processingTrackerFactory;

	public QueueTrackers() {
		_cases = Array.Empty<Conf.LabelMappingCase>();
		_busyTrackerFactory = _ => _noOpPrivate.QueueBusyTracker;
		_durationTrackerFactory = _ => _noOpShared.QueueingDurationTracker;
		_processingTrackerFactory = _ => _noOpShared.QueueProcessingTracker;
	}

	public QueueTrackers(
		Conf.LabelMappingCase[] cases,
		Func<string, IQueueBusyTracker> busyTrackerFactory,
		Func<string, IDurationMaxTracker> durationTrackerFactory,
		Func<string, IQueueProcessingTracker> processingTrackerFactory) {

		_cases = cases;
		_busyTrackerFactory = busyTrackerFactory;
		_durationTrackerFactory = durationTrackerFactory;
		_processingTrackerFactory = processingTrackerFactory;
	}

	public QueueTracker GetTrackerForQueue(string queueName) {
		var sharedTrackers = GetSharedTrackerForQueue(queueName);
		var privateTrackers = GetPrivateTrackerForLabel(sharedTrackers.Label);

		return new QueueTracker(
			sharedTrackers.Label,
			privateTrackers.QueueBusyTracker,
			sharedTrackers.QueueingDurationTracker,
			sharedTrackers.QueueProcessingTracker);
	}

	private SharedTrackers GetSharedTrackerForQueue(string queueName) {
		foreach (var @case in _cases) {
			var pattern = $"^{@case.Regex}$";
			var match = Regex.Match(input: queueName, pattern: pattern);
			if (match.Success) {
				if (string.IsNullOrWhiteSpace(@case.Label)) {
					Log.Warning(
						"Label for queue {queueName} matching pattern {pattern} was not specified. " +
						"Metrics will not be collected for this queue",
						queueName, @case.Regex);
					return _noOpShared;
				}

				var label = Regex.Replace(
					input: queueName,
					pattern: pattern,
					replacement: @case.Label);

				if (string.IsNullOrWhiteSpace(label))
					return _noOpShared;

				Log.Information(
					"Metrics matched queue {queueName} with pattern {pattern}. Label: {label}",
					queueName, @case.Regex, label);

				return GetSharedTrackerForLabel(label);
			}
		}

		Log.Information("Metrics did not match queue {queueName}. Metrics will not be collected for this queue", queueName);
		return _noOpShared;
	}

	private SharedTrackers GetSharedTrackerForLabel(string label) {
		if (!_sharedTrackers.TryGetValue(label, out var tracker)) {
			tracker = new(
				label,
				_durationTrackerFactory(label),
				_processingTrackerFactory(label));
			_sharedTrackers[label] = tracker;
		}

		return tracker;
	}

	private PrivateTrackers GetPrivateTrackerForLabel(string label) {
		return new(_busyTrackerFactory(label));
	}

	// each queue gets its own instance of the busytracker because it deals with the aggregation
	// on observation rather than on measurement. two queues trying to start/stop the same stopwatch
	// wouldn't make sense.
	private record PrivateTrackers(
		IQueueBusyTracker QueueBusyTracker);

	private record SharedTrackers(
		string Label,
		IDurationMaxTracker QueueingDurationTracker,
		IQueueProcessingTracker QueueProcessingTracker);
}
