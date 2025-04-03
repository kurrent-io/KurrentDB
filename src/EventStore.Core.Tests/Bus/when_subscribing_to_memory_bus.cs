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
public class when_subscribing_to_memory_bus {
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
	public void null_as_handler_app_should_throw_arg_null_exception() {
		Assert.Throws<ArgumentNullException>(() => _bus.Subscribe<TestMessage>(null));
	}

	[Test]
	public void but_not_publishing_messages_noone_should_handle_any_messages() {
		var multiHandler = new TestMultiHandler();
		_bus.Subscribe<TestMessage>(multiHandler);
		_bus.Subscribe<TestMessage2>(multiHandler);
		_bus.Subscribe<TestMessage3>(multiHandler);

		Assert.That(multiHandler.HandledMessages.Count == 0);
	}

	[Test]
	public async Task one_handler_to_one_message_it_should_be_handled() {
		var handler = new TestHandler<TestMessage>();
		_bus.Subscribe(handler);

		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler.HandledMessages.ContainsSingle<TestMessage>());
	}

	[Test]
	public async Task one_handler_to_multiple_messages_they_all_should_be_handled() {
		var multiHandler = new TestMultiHandler();
		_bus.Subscribe<TestMessage>(multiHandler);
		_bus.Subscribe<TestMessage2>(multiHandler);
		_bus.Subscribe<TestMessage3>(multiHandler);

		await _bus.DispatchAsync(new TestMessage());
		await _bus.DispatchAsync(new TestMessage2());
		await _bus.DispatchAsync(new TestMessage3());

		Assert.That(multiHandler.HandledMessages.ContainsSingle<TestMessage>() &&
		            multiHandler.HandledMessages.ContainsSingle<TestMessage2>() &&
		            multiHandler.HandledMessages.ContainsSingle<TestMessage3>());
	}

	[Test]
	public async Task one_handler_to_few_messages_then_only_subscribed_should_be_handled() {
		var multiHandler = new TestMultiHandler();
		_bus.Subscribe<TestMessage>(multiHandler);
		_bus.Subscribe<TestMessage3>(multiHandler);

		await _bus.DispatchAsync(new TestMessage());
		await _bus.DispatchAsync(new TestMessage2());
		await _bus.DispatchAsync(new TestMessage3());

		Assert.That(multiHandler.HandledMessages.ContainsSingle<TestMessage>() &&
		            multiHandler.HandledMessages.ContainsNo<TestMessage2>() &&
		            multiHandler.HandledMessages.ContainsSingle<TestMessage3>());
	}

	[Test]
	public async Task multiple_handlers_to_one_message_then_each_handler_should_handle_message_once() {
		var handler1 = new TestHandler<TestMessage>();
		var handler2 = new TestHandler<TestMessage>();

		_bus.Subscribe(handler1);
		_bus.Subscribe(handler2);

		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler1.HandledMessages.ContainsSingle<TestMessage>());
		Assert.That(handler2.HandledMessages.ContainsSingle<TestMessage>());
	}

	[Test]
	public async Task multiple_handlers_to_multiple_messages_then_each_handler_should_handle_subscribed_messages() {
		var handler1 = new TestMultiHandler();
		var handler2 = new TestMultiHandler();
		var handler3 = new TestMultiHandler();

		_bus.Subscribe<TestMessage>(handler1);
		_bus.Subscribe<TestMessage3>(handler1);

		_bus.Subscribe<TestMessage>(handler2);
		_bus.Subscribe<TestMessage2>(handler2);

		_bus.Subscribe<TestMessage2>(handler3);
		_bus.Subscribe<TestMessage3>(handler3);

		await _bus.DispatchAsync(new TestMessage());
		await _bus.DispatchAsync(new TestMessage2());
		await _bus.DispatchAsync(new TestMessage3());

		Assert.That(handler1.HandledMessages.ContainsSingle<TestMessage>() &&
		            handler1.HandledMessages.ContainsSingle<TestMessage3>() &&
		            handler2.HandledMessages.ContainsSingle<TestMessage>() &&
		            handler2.HandledMessages.ContainsSingle<TestMessage2>() &&
		            handler3.HandledMessages.ContainsSingle<TestMessage2>() &&
		            handler3.HandledMessages.ContainsSingle<TestMessage3>());
	}

	[Test]
	public async Task
		multiple_handlers_to_multiple_messages_then_each_handler_should_handle_only_subscribed_messages() {
		var handler1 = new TestMultiHandler();
		var handler2 = new TestMultiHandler();
		var handler3 = new TestMultiHandler();

		_bus.Subscribe<TestMessage>(handler1);
		_bus.Subscribe<TestMessage3>(handler1);

		_bus.Subscribe<TestMessage>(handler2);
		_bus.Subscribe<TestMessage2>(handler2);

		_bus.Subscribe<TestMessage2>(handler3);
		_bus.Subscribe<TestMessage3>(handler3);


		await _bus.DispatchAsync(new TestMessage());
		await _bus.DispatchAsync(new TestMessage2());
		await _bus.DispatchAsync(new TestMessage3());


		Assert.That(handler1.HandledMessages.ContainsSingle<TestMessage>() &&
		            handler1.HandledMessages.ContainsNo<TestMessage2>() &&
		            handler1.HandledMessages.ContainsSingle<TestMessage3>() &&
		            handler2.HandledMessages.ContainsSingle<TestMessage>() &&
		            handler2.HandledMessages.ContainsSingle<TestMessage2>() &&
		            handler2.HandledMessages.ContainsNo<TestMessage3>() &&
		            handler3.HandledMessages.ContainsNo<TestMessage>() &&
		            handler3.HandledMessages.ContainsSingle<TestMessage2>() &&
		            handler3.HandledMessages.ContainsSingle<TestMessage3>());
	}

	[Test /*, Ignore("This logic is confused when having hierarchy flattening on subscription in InMemoryBus.")*/]
	public async Task same_handler_to_same_message_few_times_then_message_should_be_handled_only_once() {
		var handler = new TestHandler<TestMessage>();
		_bus.Subscribe(handler);
		_bus.Subscribe(handler);
		_bus.Subscribe(handler);

		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler.HandledMessages.ContainsSingle<TestMessage>());
	}
}
