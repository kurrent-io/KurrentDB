// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.TimerService;

public interface IScheduler : IDisposable {
	void Stop();
	void Schedule(TimeSpan after, Action<IScheduler, object> callback, object state);
}
