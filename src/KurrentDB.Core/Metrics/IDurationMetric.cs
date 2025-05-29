// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Time;

namespace KurrentDB.Core.Metrics;

public interface IDurationMetric {
	public static IDurationMetric NoOp => NoopDurationMetric.Instance;

	public Instant Record(Instant start, KeyValuePair<string, object> tag1, KeyValuePair<string, object> tag2);
}

file sealed class NoopDurationMetric : IDurationMetric {
	public static NoopDurationMetric Instance { get; } = new();

	public Instant Record(Instant start, KeyValuePair<string, object> tag1, KeyValuePair<string, object> tag2) {
		return Instant.Now;
	}
}
