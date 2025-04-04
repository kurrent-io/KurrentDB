// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Time;

namespace EventStore.Core.XUnit.Tests.Metrics;

internal class FakeClock : IClock {
	public Instant Now => Instant.FromSeconds(SecondsSinceEpoch);

	public long SecondsSinceEpoch { get; set; }

	public void AdvanceSeconds(long seconds) {
		SecondsSinceEpoch += seconds;
	}
}
