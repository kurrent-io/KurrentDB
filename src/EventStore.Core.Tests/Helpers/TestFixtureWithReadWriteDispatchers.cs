// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.Tests.Services.TimeService;
using NUnit.Framework;
using System.Linq;
using EventStore.Core.Metrics;
using DotNext;

namespace EventStore.Core.Tests.Helpers;

public abstract class TestFixtureWithReadWriteDispatchers {
	protected SynchronousScheduler _bus;
	protected IQueuedHandler _publisher;

	protected RequestResponseDispatcher<ClientMessage.DeleteStream, ClientMessage.DeleteStreamCompleted>
		_streamDispatcher;

	protected RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted>
		_writeDispatcher;

	protected ReadDispatcher _readDispatcher;

	protected TestHandler<Message> _consumer;
	protected IODispatcher _ioDispatcher;
	protected ManualQueue _queue;
	protected ManualQueue[] _otherQueues;
	protected FakeTimeProvider _timeProvider;
	private IEnvelope _envelope;

	protected IEnvelope Envelope {
		get {
			if (_envelope == null)
				_envelope = GetInputQueue();
			return _envelope;
		}
	}

	protected List<Message> HandledMessages {
		get { return _consumer.HandledMessages; }
	}

	[SetUp]
	public void setup0() {
		_envelope = null;
		_timeProvider = new FakeTimeProvider();
		_bus = new SynchronousScheduler();
		_publisher = new QueuedHandlerThreadPool(_bus,
			"TestQueue",
			new QueueStatsManager(),
			new QueueTrackers(), watchSlowMsg: false);
		_publisher.Start();
		_consumer = new TestHandler<Message>();
		_bus.Subscribe(_consumer);
		_queue = GiveInputQueue();
		_otherQueues = null;
		_ioDispatcher = new IODispatcher(_bus, GetInputQueue());
		_readDispatcher = _ioDispatcher.BackwardReader;
		_writeDispatcher = _ioDispatcher.Writer;
		_streamDispatcher = _ioDispatcher.StreamDeleter;

		_bus.Subscribe(_ioDispatcher.ForwardReader);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
		_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher.BackwardReader);
		_bus.Subscribe(_ioDispatcher.ForwardReader);
		_bus.Subscribe(_ioDispatcher.Writer);
		_bus.Subscribe(_ioDispatcher.StreamDeleter);
		_bus.Subscribe(_ioDispatcher.Awaker);
		_bus.Subscribe<IODispatcherDelayedMessage>(_ioDispatcher);
		_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher);
	}

	protected virtual ManualQueue GiveInputQueue() {
		return null;
	}

	protected IPublisher GetInputQueue() {
		return _queue.As<IPublisher>() ?? _bus;
	}

	protected void DisableTimer() {
		_queue.DisableTimer();
	}

	protected void EnableTimer() {
		_queue.EnableTimer();
	}

	protected void WhenLoop() {
		_queue.Process();
		var steps = PreWhen().Concat(When());
		WhenLoop(steps);
	}

	protected void WhenLoop(IEnumerable<WhenStep> steps) {
		foreach (var step in steps) {
			_timeProvider.AddToUtcTime(TimeSpan.FromMilliseconds(10));
			if (step.Action != null) {
				step.Action();
			}

			foreach (var message in step) {
				if (message != null)
					_queue.Publish(message);
			}

			_queue.ProcessTimer();
			if (_otherQueues != null)
				foreach (var other in _otherQueues)
					other.ProcessTimer();

			var count = 1;
			var total = 0;
			while (count > 0) {
				count = 0;
				count += _queue.ProcessNonTimer();
				if (_otherQueues != null)
					foreach (var other in _otherQueues)
						count += other.ProcessNonTimer();
				total += count;
				if (total > 2000)
					throw new Exception("Infinite loop?");
			}

			// process final timer messages
		}

		_queue.Process();
		if (_otherQueues != null)
			foreach (var other in _otherQueues)
				other.Process();
	}

	public static T EatException<T>(Func<T> func, T defaultValue = default(T)) {
		try {
			return func();
		} catch (Exception) {
			return defaultValue;
		}
	}

	public sealed class WhenStep : IEnumerable<Message> {
		public readonly Action Action;
		public readonly Message Message;
		public readonly IEnumerable<Message> Messages;

		private WhenStep(Message message) {
			Message = message;
		}

		internal WhenStep(IEnumerable<Message> messages) {
			Messages = messages;
		}

		public WhenStep(params Message[] messages) {
			Messages = messages;
		}

		public WhenStep(Action action) {
			Action = action;
		}

		internal WhenStep() {
		}

		public static implicit operator WhenStep(Message message) {
			return new WhenStep(message);
		}

		public IEnumerator<Message> GetEnumerator() {
			return GetMessages().GetEnumerator();
		}

		private IEnumerable<Message> GetMessages() {
			if (Message != null)
				yield return Message;
			else if (Messages != null)
				foreach (var message in Messages)
					yield return message;
			else yield return null;
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return GetEnumerator();
		}
	}

	protected virtual IEnumerable<WhenStep> PreWhen() {
		yield break;
	}

	protected virtual IEnumerable<WhenStep> When() {
		yield break;
	}

	public readonly WhenStep Yield = new WhenStep();
}

public static class TestUtils {
	public static TestFixtureWithReadWriteDispatchers.WhenStep ToSteps(
		this IEnumerable<TestFixtureWithReadWriteDispatchers.WhenStep> steps) {
		return new TestFixtureWithReadWriteDispatchers.WhenStep(steps.SelectMany(v => v));
	}
}
