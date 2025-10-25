// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Services.TimerService;

namespace KurrentDB.Core.Tests.Services.TimeService;

public class FakeTimeProvider : ITimeProvider {
	public DateTime UtcNow { get; private set; }
	public DateTime LocalTime { get; private set; }

	public FakeTimeProvider() {
		UtcNow = DateTime.UtcNow;
		LocalTime = DateTime.Now;
	}

	public void SetNewUtcTime(DateTime newTime) {
		UtcNow = newTime;
	}

	public void AddToUtcTime(TimeSpan timeSpan) {
		UtcNow = UtcNow.Add(timeSpan);
	}

	public void AddToLocalTime(TimeSpan timeSpan) {
		UtcNow = LocalTime.Add(timeSpan);
	}
}
