// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;

namespace KurrentDB.SecondaryIndexing.Indexes.Diagnostics;

public interface IQueryTracker {
	IDisposable Start<TQuery>();
}

public class QueryTracker(DurationMaxMetric durationMaxMetric, int expectedScrapeInterval) : IQueryTracker {
	public static readonly IQueryTracker NoOp = new NoOpQueryTracker();
	private readonly ConcurrentDictionary<string, DurationMaxTracker> _durationMaxTrackers = new();

	public IDisposable Start<TQuery>() => new QueryTrackerInstance(Instant.Now, GetDurationMaxTracker(typeof(TQuery).Name));

	private DurationMaxTracker GetDurationMaxTracker(string queryType) =>
		_durationMaxTrackers.GetOrAdd(queryType, key => new DurationMaxTracker(durationMaxMetric, key, expectedScrapeInterval));
}

file sealed class QueryTrackerInstance(Instant start, DurationMaxTracker durationMaxTracker) : IDisposable {
	public void Dispose() {
		lock (durationMaxTracker) { // multiple threads cannot record simultaneously
			durationMaxTracker.RecordNow(start);
		}
	}
}

file sealed class NoOpQueryTracker : IQueryTracker {
	public IDisposable Start<TQuery>() => null!;
}
