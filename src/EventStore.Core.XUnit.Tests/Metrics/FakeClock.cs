// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Time;

namespace EventStore.Core.XUnit.Tests.Metrics;

internal class FakeClock : IClock {
	public Instant Now => Instant.FromSeconds(SecondsSinceEpoch);

	public long SecondsSinceEpoch { get; set; }

	public void AdvanceSeconds(long seconds) {
		SecondsSinceEpoch += seconds;
	}
}
