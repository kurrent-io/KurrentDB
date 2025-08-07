// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Metrics;

namespace KurrentDB.SecondaryIndexing.Diagnostics;

public class SecondaryIndexTrackers {
	public IDurationTracker Commit { get; set; } = new DurationTracker.NoOp();
}
