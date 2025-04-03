// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;

namespace EventStore.Core.Services.TimerService;

public sealed class RealTimer : ITimer {
	private Action _callback;
	private readonly Timer _timer;

	public RealTimer() {
		_timer = new Timer(InvokeCallback, null, Timeout.Infinite, Timeout.Infinite);
	}

	private void InvokeCallback(object state) {
		_callback?.Invoke();
	}

	public void FireIn(int milliseconds, Action callback) {
		_callback = callback;
		var dueTime = milliseconds == Timeout.Infinite ? Timeout.Infinite : Math.Max(0, milliseconds);
		_timer.Change(dueTime, Timeout.Infinite);
	}

	public void Dispose() {
		_timer.Dispose();
	}
}
