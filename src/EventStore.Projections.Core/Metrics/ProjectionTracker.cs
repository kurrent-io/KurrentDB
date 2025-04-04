// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Metrics;

public class ProjectionTracker : IProjectionTracker {
	private ProjectionStatistics[] _currentStats = [];

	public void OnNewStats(ProjectionStatistics[] newStats) {
		_currentStats = newStats ?? [];
	}

	public IEnumerable<Measurement<long>> ObserveEventsProcessed() =>
		_currentStats.Select(x =>
			new Measurement<long>(
				x.EventsProcessedAfterRestart,
				[
					new("projection", x.Name)
				]));

	public IEnumerable<Measurement<float>> ObserveProgress() =>
		_currentStats.Select(x =>
			new Measurement<float>(
				x.Progress / 100.0f,
				[
					new("projection", x.Name)
				]));

	public IEnumerable<Measurement<long>> ObserveRunning() =>
		_currentStats.Select(x => {
			var projectionRunning = x.Status.Equals("running", StringComparison.CurrentCultureIgnoreCase)
				? 1
				: 0;

			return new Measurement<long>(
				projectionRunning, [
					new("projection", x.Name)
				]);
		});

	public IEnumerable<Measurement<long>> ObserveStatus() {
		foreach (var statistics in _currentStats) {
			var projectionRunning = 0;
			var projectionFaulted = 0;
			var projectionStopped = 0;

			switch (statistics.Status.ToLower()) {
				case "running":
					projectionRunning = 1;
					break;
				case "stopped":
					projectionStopped = 1;
					break;
				case "faulted":
					projectionFaulted = 1;
					break;
			}

			yield return new(projectionRunning, [
				new("projection", statistics.Name),
				new("status", "Running"),
			]);

			yield return new(projectionFaulted, [
				new("projection", statistics.Name),
				new("status", "Faulted"),
			]);

			yield return new(projectionStopped, [
				new("projection", statistics.Name),
				new("status", "Stopped"),
			]);
		}
	}
}
