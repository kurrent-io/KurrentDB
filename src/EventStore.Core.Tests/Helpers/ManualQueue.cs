// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using System.Linq;

namespace EventStore.Core.Tests.Helpers;

public class ManualQueue : IPublisher, IHandle<TimerMessage.Schedule> {
	private readonly Queue<Message> _queue = new();
	private readonly SynchronousScheduler _bus;
	private readonly ITimeProvider _time;
	private readonly List<InternalSchedule> _timerQueue = new();
	private bool _timerDisabled;

	public ManualQueue(SynchronousScheduler bus, ITimeProvider time) {
		_bus = bus;
		_time = time;
		_bus.Subscribe<TimerMessage.Schedule>(this);
	}

	public void Publish(Message message) {
		_queue.Enqueue(message);
	}

	public int Process() {
		ProcessTimer();
		return ProcessNonTimer();
	}

	public int ProcessNonTimer() {
		var count = 0;
		while (_queue.Count > 0) {
			var message = _queue.Dequeue();
			_bus.Publish(message);
			count++;
			if (count > 1000)
				throw new Exception("Possible infinite message loop");
		}

		return count;
	}

	public void ProcessTimer() {
		if (!_timerDisabled) {
			var orderedTimerMessages = _timerQueue.OrderBy(v => v.Scheduled.Add(v.Message.TriggerAfter)).ToArray();
			_timerQueue.Clear();
			foreach (var timerMessage in orderedTimerMessages) {
				if (timerMessage.Scheduled.Add(timerMessage.Message.TriggerAfter) <= _time.UtcNow)
					timerMessage.Message.Reply();
				else
					_timerQueue.Add(timerMessage);
			}
		}
	}

	public IEnumerable<TimerMessage.Schedule> TimerMessagesOfType<T>() {
		foreach (var message in _timerQueue) {
			if (message.Message.ReplyMessage is T) {
				yield return message.Message;
			}
		}
	}

	public void DisableTimer() {
		_timerDisabled = true;
	}

	public void EnableTimer(bool process = true) {
		_timerDisabled = false;
		if (process)
			Process();
	}

	private class InternalSchedule {
		public readonly TimerMessage.Schedule Message;
		public readonly DateTime Scheduled;

		public InternalSchedule(TimerMessage.Schedule message, DateTime scheduled) {
			Message = message;
			Scheduled = scheduled;
		}
	}

	public void Handle(TimerMessage.Schedule message) {
		_timerQueue.Add(new InternalSchedule(message, _time.UtcNow));
	}
}
