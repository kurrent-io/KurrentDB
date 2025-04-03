// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus;

[TestFixture]
public class when_unsubscribing_from_memory_bus {
	private InMemoryBus _bus;

	[SetUp]
	public void SetUp() {
		_bus = InMemoryBus.CreateTest(false);
	}

	[TearDown]
	public void TearDown() {
		_bus = null;
	}

	[Test]
	public void null_as_handler_app_should_throw() {
		Assert.Throws<ArgumentNullException>(() => _bus.Unsubscribe<TestMessage>(null));
	}

	[Test]
	public void not_subscribed_handler_app_doesnt_throw() {
		var handler = new TestHandler<TestMessage>();
		Assert.DoesNotThrow(() => _bus.Unsubscribe<TestMessage>(handler));
	}

	[Test]
	public void same_handler_from_same_message_multiple_times_app_doesnt_throw() {
		var handler = new TestHandler<TestMessage>();
		Assert.DoesNotThrow(() => {
			_bus.Unsubscribe<TestMessage>(handler);
			_bus.Unsubscribe<TestMessage>(handler);
			_bus.Unsubscribe<TestMessage>(handler);
		});
	}

	[Test]
	public void multihandler_from_single_message_app_doesnt_throw() {
		var handler = new TestMultiHandler();
		_bus.Subscribe<TestMessage>(handler);
		_bus.Subscribe<TestMessage2>(handler);
		_bus.Subscribe<TestMessage3>(handler);

		Assert.DoesNotThrow(() => _bus.Unsubscribe<TestMessage>(handler));
	}

	[Test]
	public async Task handler_from_message_it_should_not_handle_this_message_anymore() {
		var handler = new TestHandler<TestMessage>();
		_bus.Subscribe(handler);

		_bus.Unsubscribe(handler);
		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler.HandledMessages.IsEmpty());
	}

	[Test]
	public async Task handler_from_multiple_messages_they_all_should_not_be_handled_anymore() {
		var handler = new TestMultiHandler();
		_bus.Subscribe<TestMessage>(handler);
		_bus.Subscribe<TestMessage2>(handler);
		_bus.Subscribe<TestMessage3>(handler);

		_bus.Unsubscribe<TestMessage>(handler);
		_bus.Unsubscribe<TestMessage2>(handler);
		_bus.Unsubscribe<TestMessage3>(handler);

		await _bus.DispatchAsync(new TestMessage());
		await _bus.DispatchAsync(new TestMessage2());
		await _bus.DispatchAsync(new TestMessage3());

		Assert.That(handler.HandledMessages.ContainsNo<TestMessage>() &&
		            handler.HandledMessages.ContainsNo<TestMessage2>() &&
		            handler.HandledMessages.ContainsNo<TestMessage3>());
	}

	[Test]
	public async Task handler_from_message_it_should_not_handle_this_message_anymore_and_still_handle_other_messages() {
		var handler = new TestMultiHandler();
		_bus.Subscribe<TestMessage>(handler);
		_bus.Subscribe<TestMessage2>(handler);
		_bus.Subscribe<TestMessage3>(handler);

		_bus.Unsubscribe<TestMessage>(handler);

		await _bus.DispatchAsync(new TestMessage());
		await _bus.DispatchAsync(new TestMessage2());
		await _bus.DispatchAsync(new TestMessage3());

		Assert.That(handler.HandledMessages.ContainsNo<TestMessage>() &&
		            handler.HandledMessages.ContainsSingle<TestMessage2>() &&
		            handler.HandledMessages.ContainsSingle<TestMessage3>());
	}

	[Test]
	public async Task one_handler_and_leaving_others_subscribed_only_others_should_handle_message() {
		var handler1 = new TestHandler<TestMessage>();
		var handler2 = new TestHandler<TestMessage>();
		var handler3 = new TestHandler<TestMessage>();

		_bus.Subscribe(handler1);
		_bus.Subscribe(handler2);
		_bus.Subscribe(handler3);

		_bus.Unsubscribe(handler1);
		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler1.HandledMessages.ContainsNo<TestMessage>() &&
		            handler2.HandledMessages.ContainsSingle<TestMessage>() &&
		            handler3.HandledMessages.ContainsSingle<TestMessage>());
	}

	[Test]
	public async Task all_handlers_from_message_noone_should_handle_message() {
		var handler1 = new TestHandler<TestMessage>();
		var handler2 = new TestHandler<TestMessage>();
		var handler3 = new TestHandler<TestMessage>();

		_bus.Subscribe(handler1);
		_bus.Subscribe(handler2);
		_bus.Subscribe(handler3);

		_bus.Unsubscribe(handler1);
		_bus.Unsubscribe(handler2);
		_bus.Unsubscribe(handler3);
		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler1.HandledMessages.ContainsNo<TestMessage>() &&
		            handler2.HandledMessages.ContainsNo<TestMessage>() &&
		            handler3.HandledMessages.ContainsNo<TestMessage>());
	}

	[Test]
	public async Task handlers_after_publishing_message_all_is_still_done_correctly() {
		var handler1 = new TestHandler<TestMessage>();
		var handler2 = new TestHandler<TestMessage>();
		var handler3 = new TestHandler<TestMessage>();

		_bus.Subscribe(handler1);
		_bus.Subscribe(handler2);
		_bus.Subscribe(handler3);

		await _bus.DispatchAsync(new TestMessage());
		handler1.HandledMessages.Clear();
		handler2.HandledMessages.Clear();
		handler3.HandledMessages.Clear();

		//just to ensure
		Assert.That(handler1.HandledMessages.ContainsNo<TestMessage>() &&
		            handler2.HandledMessages.ContainsNo<TestMessage>() &&
		            handler3.HandledMessages.ContainsNo<TestMessage>());

		_bus.Unsubscribe(handler1);
		_bus.Unsubscribe(handler2);
		_bus.Unsubscribe(handler3);
		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler1.HandledMessages.ContainsNo<TestMessage>() &&
		            handler2.HandledMessages.ContainsNo<TestMessage>() &&
		            handler3.HandledMessages.ContainsNo<TestMessage>());
	}
}
