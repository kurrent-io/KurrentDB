// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Messages;

namespace EventStore.Core.Services.TimerService;

/// <summary>
/// Timer service uses scheduler that is expected to be already running
/// when it is passed to constructor and stopped on the disposal. This is done to
/// make sure that we can handle timeouts and callbacks any time
/// (even during system shutdowns and initialization)
/// </summary>
public class TimerService(IScheduler scheduler) : IDisposable,
	IHandle<SystemMessage.BecomeShutdown>,
	IHandle<TimerMessage.Schedule> {
	public void Handle(SystemMessage.BecomeShutdown message) {
		scheduler.Stop();
	}

	public void Handle(TimerMessage.Schedule message) {
		scheduler.Schedule(
			message.TriggerAfter,
			static (scheduler, state) => OnTimerCallback(scheduler, state),
			message);
	}

	private static void OnTimerCallback(IScheduler scheduler, object state) {
		var msg = (TimerMessage.Schedule)state;
		msg.Reply();
	}

	public void Dispose() {
		scheduler.Dispose();
	}
}
