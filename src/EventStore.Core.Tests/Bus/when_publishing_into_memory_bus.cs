using System;
using EventStore.Core.Bus;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus {
	[TestFixture]
	public class when_publishing_into_memory_bus {
		private InMemoryBus _bus;

		[SetUp]
		public void SetUp() {
			_bus = new InMemoryBus("test_bus", watchSlowMsg: false);
		}

		[TearDown]
		public void TearDown() {
			_bus = null;
		}

		[Test, Ignore("We do not check each message for null for performance reasons.")]
		public void null_message_app_should_throw() {
			Assert.Throws<ArgumentNullException>(() => _bus.Publish(null));
		}

		[Test]
		public void unsubscribed_messages_noone_should_handle_it() {
			var handler1 = new TestHandler<TestMessage>();
			var handler2 = new TestHandler<TestMessage2>();
			var handler3 = new TestHandler<TestMessage3>();

			_bus.Publish(new TestMessage());
			_bus.Publish(new TestMessage2());
			_bus.Publish(new TestMessage3());

			Assert.That(handler1.HandledMessages.Count == 0
						&& handler2.HandledMessages.Count == 0
						&& handler3.HandledMessages.Count == 0);
		}

		[Test]
		public void any_message_no_other_messages_should_be_published() {
			var handler1 = new TestHandler<TestMessage>();
			var handler2 = new TestHandler<TestMessage2>();

			_bus.Subscribe<TestMessage>(handler1);
			_bus.Subscribe<TestMessage2>(handler2);

			_bus.Publish(new TestMessage());

			Assert.That(handler1.HandledMessages.ContainsSingle<TestMessage>() && handler2.HandledMessages.Count == 0);
		}

		[Test]
		public void same_message_n_times_it_should_be_handled_n_times() {
			var handler = new TestHandler<TestMessageWithId>();
			var message = new TestMessageWithId(11);

			_bus.Subscribe<TestMessageWithId>(handler);

			_bus.Publish(message);
			_bus.Publish(message);
			_bus.Publish(message);

			Assert.That(handler.HandledMessages.ContainsN<TestMessageWithId>(3, mes => mes.Id == 11));
		}

		[Test]
		public void multiple_messages_of_same_type_they_all_should_be_delivered() {
			var handler = new TestHandler<TestMessageWithId>();
			var message1 = new TestMessageWithId(1);
			var message2 = new TestMessageWithId(2);
			var message3 = new TestMessageWithId(3);

			_bus.Subscribe<TestMessageWithId>(handler);

			_bus.Publish(message1);
			_bus.Publish(message2);
			_bus.Publish(message3);

			Assert.That(handler.HandledMessages.ContainsSingle<TestMessageWithId>(mes => mes.Id == 1));
			Assert.That(handler.HandledMessages.ContainsSingle<TestMessageWithId>(mes => mes.Id == 2));
			Assert.That(handler.HandledMessages.ContainsSingle<TestMessageWithId>(mes => mes.Id == 3));
		}

		[Test]
		public void message_of_child_type_then_all_subscribed_handlers_of_parent_type_should_handle_message() {
			var parentHandler = new TestHandler<ParentTestMessage>();
			_bus.Subscribe<ParentTestMessage>(parentHandler);

			_bus.Publish(new ChildTestMessage());

			Assert.That(parentHandler.HandledMessages.ContainsSingle<ChildTestMessage>());
		}

		[Test]
		public void message_of_parent_type_then_no_subscribed_handlers_of_child_type_should_handle_message() {
			var childHandler = new TestHandler<ChildTestMessage>();
			_bus.Subscribe<ChildTestMessage>(childHandler);

			_bus.Publish(new ParentTestMessage());

			Assert.That(childHandler.HandledMessages.ContainsNo<ParentTestMessage>());
		}

		[Test]
		public void message_of_grand_child_type_then_all_subscribed_handlers_of_base_types_should_handle_message() {
			var parentHandler = new TestHandler<ParentTestMessage>();
			var childHandler = new TestHandler<ChildTestMessage>();

			_bus.Subscribe<ParentTestMessage>(parentHandler);
			_bus.Subscribe<ChildTestMessage>(childHandler);

			_bus.Publish(new GrandChildTestMessage());

			Assert.That(parentHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>() &&
						childHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>());
		}

		[Test]
		public void
			message_of_grand_child_type_then_all_subscribed_handlers_of_parent_types_including_grand_child_handler_should_handle_message() {
			var parentHandler = new TestHandler<ParentTestMessage>();
			var childHandler = new TestHandler<ChildTestMessage>();
			var grandChildHandler = new TestHandler<GrandChildTestMessage>();

			_bus.Subscribe<ParentTestMessage>(parentHandler);
			_bus.Subscribe<ChildTestMessage>(childHandler);
			_bus.Subscribe<GrandChildTestMessage>(grandChildHandler);

			_bus.Publish(new GrandChildTestMessage());

			Assert.That(parentHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>() &&
						childHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>() &&
						grandChildHandler.HandledMessages.ContainsSingle<GrandChildTestMessage>());
		}
	}
}
