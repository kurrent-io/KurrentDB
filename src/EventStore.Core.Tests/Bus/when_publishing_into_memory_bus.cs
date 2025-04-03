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
public class when_publishing_into_memory_bus {
	private InMemoryBus _bus;

	[SetUp]
	public void SetUp() {
		_bus = InMemoryBus.CreateTest(false);
	}

	[TearDown]
	public void TearDown() {
		_bus = null;
	}

	[Test, Ignore("We do not check each message for null for performance reasons.")]
	public void null_message_app_should_throw() {
		Assert.ThrowsAsync<ArgumentNullException>(async () => await _bus.DispatchAsync(null));
	}

	[Test]
	public async Task unsubscribed_messages_noone_should_handle_it() {
		var handler1 = new TestHandler<TestMessage>();
		var handler2 = new TestHandler<TestMessage2>();
		var handler3 = new TestHandler<TestMessage3>();

		await _bus.DispatchAsync(new TestMessage());
		await _bus.DispatchAsync(new TestMessage2());
		await _bus.DispatchAsync(new TestMessage3());

		Assert.That(handler1.HandledMessages.Count == 0
		            && handler2.HandledMessages.Count == 0
		            && handler3.HandledMessages.Count == 0);
	}

	[Test]
	public async Task any_message_no_other_messages_should_be_published() {
		var handler1 = new TestHandler<TestMessage>();
		var handler2 = new TestHandler<TestMessage2>();

		_bus.Subscribe(handler1);
		_bus.Subscribe(handler2);

		await _bus.DispatchAsync(new TestMessage());

		Assert.That(handler1.HandledMessages.ContainsSingle<TestMessage>() && handler2.HandledMessages.Count == 0);
	}

	[Test]
	public async Task same_message_n_times_it_should_be_handled_n_times() {
		var handler = new TestHandler<TestMessageWithId>();
		var message = new TestMessageWithId(11);

		_bus.Subscribe(handler);

		await _bus.DispatchAsync(message);
		await _bus.DispatchAsync(message);
		await _bus.DispatchAsync(message);

		Assert.That(handler.HandledMessages.ContainsN<TestMessageWithId>(3, mes => mes.Id == 11));
	}

	[Test]
	public async Task multiple_messages_of_same_type_they_all_should_be_delivered() {
		var handler = new TestHandler<TestMessageWithId>();
		var message1 = new TestMessageWithId(1);
		var message2 = new TestMessageWithId(2);
		var message3 = new TestMessageWithId(3);

		_bus.Subscribe(handler);

		await _bus.DispatchAsync(message1);
		await _bus.DispatchAsync(message2);
		await _bus.DispatchAsync(message3);

		Assert.That(handler.HandledMessages.ContainsSingle<TestMessageWithId>(mes => mes.Id == 1));
		Assert.That(handler.HandledMessages.ContainsSingle<TestMessageWithId>(mes => mes.Id == 2));
		Assert.That(handler.HandledMessages.ContainsSingle<TestMessageWithId>(mes => mes.Id == 3));
	}

	[Test]
	public async Task message_of_child_type_then_all_subscribed_handlers_of_parent_type_should_handle_message() {
		var parentHandler = new TestHandler<ParentTestMessage>();
		_bus.Subscribe(parentHandler);

		await _bus.DispatchAsync(new ChildTestMessage());

		Assert.That(parentHandler.HandledMessages.ContainsSingle<ChildTestMessage>());
	}

	[Test]
	public async Task message_of_parent_type_then_no_subscribed_handlers_of_child_type_should_handle_message() {
		var childHandler = new TestHandler<ChildTestMessage>();
		_bus.Subscribe(childHandler);

		await _bus.DispatchAsync(new ParentTestMessage());

		Assert.That(childHandler.HandledMessages.ContainsNo<ParentTestMessage>());
	}

	[Test]
	public async Task message_of_grand_child_type_then_all_subscribed_handlers_of_base_types_should_handle_message() {
		var parentHandler = new TestHandler<ParentTestMessage>();
		var childHandler = new TestHandler<ChildTestMessage>();

		_bus.Subscribe(parentHandler);
		_bus.Subscribe(childHandler);

		await _bus.DispatchAsync(new GrandChildTestMessage());

		Assert.That(parentHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>() &&
		            childHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>());
	}

	[Test]
	public async Task
		message_of_grand_child_type_then_all_subscribed_handlers_of_parent_types_including_grand_child_handler_should_handle_message() {
		var parentHandler = new TestHandler<ParentTestMessage>();
		var childHandler = new TestHandler<ChildTestMessage>();
		var grandChildHandler = new TestHandler<GrandChildTestMessage>();

		_bus.Subscribe(parentHandler);
		_bus.Subscribe(childHandler);
		_bus.Subscribe(grandChildHandler);

		await _bus.DispatchAsync(new GrandChildTestMessage());

		Assert.That(parentHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>() &&
		            childHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>() &&
		            grandChildHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>());
	}
}
