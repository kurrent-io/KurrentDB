// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Metrics;

namespace EventStore.Core.TransactionLog.Scavenging;

public interface IScavengeStatusTracker {
	IDisposable StartActivity(string name);
}

public class ScavengeStatusTracker : IScavengeStatusTracker {
	private static ActivityStatusSubMetric _subMetric;

	public ScavengeStatusTracker(StatusMetric metric) {
		_subMetric = new("Scavenge", metric);
	}

	public IDisposable StartActivity(string name) =>
		_subMetric?.StartActivity(name + " Phase");

	public class NoOp : IScavengeStatusTracker {
		public IDisposable StartActivity(string name) => null;
	}
}
