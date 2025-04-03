// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Metrics;

namespace EventStore.Core.Index;

public interface IIndexStatusTracker {
	IDisposable StartOpening();
	IDisposable StartRebuilding();
	IDisposable StartInitializing();
	IDisposable StartMerging();
	IDisposable StartScavenging();
}

public class IndexStatusTracker : IIndexStatusTracker {
	private readonly ActivityStatusSubMetric _metric;

	public IndexStatusTracker(StatusMetric metric) {
		_metric = new("Index", metric);
	}

	public IDisposable StartOpening() => _metric.StartActivity("Opening");
	public IDisposable StartRebuilding() => _metric.StartActivity("Rebuilding");
	public IDisposable StartInitializing() => _metric.StartActivity("Initializing");
	public IDisposable StartMerging() => _metric.StartActivity("Merging");
	public IDisposable StartScavenging() => _metric.StartActivity("Scavenging");

	public class NoOp : IIndexStatusTracker {
		public IDisposable StartOpening() => null;
		public IDisposable StartRebuilding() => null;
		public IDisposable StartInitializing() => null;
		public IDisposable StartMerging() => null;
		public IDisposable StartScavenging() => null;
	}
}
