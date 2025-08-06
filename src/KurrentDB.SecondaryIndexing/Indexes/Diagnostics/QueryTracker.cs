// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;

namespace KurrentDB.SecondaryIndexing.Indexes.Diagnostics;

public interface IQueryTracker {
	IDisposable Start<TQuery>();
	IQueryTracker DontTrack { get; }
}

public class QueryTracker(DurationMetric durationMetric) : IQueryTracker {
	public static readonly IQueryTracker NoOp = new NoOpQueryTracker();
	public IQueryTracker DontTrack => NoOp;
	public IDisposable Start<TQuery>() => new QueryTrackerInstance(Instant.Now, durationMetric, typeof(TQuery).Name);
}

file sealed class QueryTrackerInstance(Instant start, DurationMetric durationMetric, string queryType) : IDisposable {
	public void Dispose() {
		durationMetric.Record(start, new KeyValuePair<string, object>("name", queryType));
	}
}

file sealed class NoOpQueryTracker : IQueryTracker {
	public IDisposable Start<TQuery>() => null!;
	public IQueryTracker DontTrack => this;
}
