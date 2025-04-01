// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.TimeProviders;

namespace EventStore.AutoScavenge.Tests.TimeProviders;

public class FakeTimeProvider : ITimeProvider {
	private DateTime _inner = DateTime.Now.Date;

	public DateTime Now => _inner;

	public void FastForward(TimeSpan timeSpan) {
		_inner = _inner.Add(timeSpan);
	}

	public DateTime NowThenFastForward(TimeSpan timeSpan) {
		var now = _inner;
		_inner = _inner.Add(timeSpan);
		return now;
	}
}
