// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Services.PersistentSubscription;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
public class StreamBufferTests {
	private EventSource _eventSource;

	public StreamBufferTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void adding_read_message_in_correct_order() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddReadMessage(BuildMessageAt(0));
		Assert.AreEqual(1, buffer.BufferCount);
		OutstandingMessage message = buffer.Scan().First().Message;
		Assert.AreEqual(GetEventIdFor(0), message.EventId);
		Assert.IsFalse(buffer.Live);
	}

	[Test]
	public void adding_multiple_read_message_in_correct_order() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddReadMessage(BuildMessageAt(0));
		buffer.AddReadMessage(BuildMessageAt(1));
		Assert.AreEqual(2, buffer.BufferCount);
		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(0), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(1, buffer.BufferCount);
		messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(1), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
		Assert.IsFalse(buffer.Live);
	}


	[Test]
	public void adding_multiple_read_message_in_wrong_order() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddReadMessage(BuildMessageAt(1));
		buffer.AddReadMessage(BuildMessageAt(0));
		Assert.AreEqual(2, buffer.BufferCount);
		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(1), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(1, buffer.BufferCount);
		messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(0), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
		Assert.IsFalse(buffer.Live);
	}

	[Test]
	public void adding_multiple_same_read_message() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddReadMessage(BuildMessageAt(0));
		buffer.AddReadMessage(BuildMessageAt(0));
		Assert.AreEqual(2, buffer.BufferCount);
		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(0), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(1, buffer.BufferCount);
		messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(0), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
		Assert.IsFalse(buffer.Live);
	}

	[Test]
	public void adding_messages_to_read_after_same_on_live_switches_to_live() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddLiveMessage(BuildMessageAt(0));
		buffer.AddReadMessage(BuildMessageAt(0));
		Assert.IsTrue(buffer.Live);
		Assert.AreEqual(1, buffer.BufferCount);
		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(0), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
	}

	[Test]
	public void adding_messages_to_read_after_later_live_does_not_switch() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddLiveMessage(BuildMessageAt(5));
		buffer.AddReadMessage(BuildMessageAt(0));
		Assert.IsFalse(buffer.Live);
		Assert.AreEqual(1, buffer.BufferCount);
		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(0), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
	}

	[Test]
	public void adding_messages_to_live_without_start_from_beginning() {
		var buffer = new StreamBuffer(10, 10, null, false);
		buffer.AddLiveMessage(BuildMessageAt(6));
		buffer.AddLiveMessage(BuildMessageAt(7));
		Assert.IsTrue(buffer.Live);
		Assert.AreEqual(2, buffer.BufferCount);
		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(6), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(1, buffer.BufferCount);
		messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(7), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
	}

	[Test]
	public void adding_messages_with_lower_in_live() {
		var buffer = new StreamBuffer(10, 10, null, true);
		var id = Guid.NewGuid();
		buffer.AddLiveMessage(BuildMessageAt(5));
		buffer.AddLiveMessage(BuildMessageAt(6));
		buffer.AddLiveMessage(BuildMessageAt(7, id));
		buffer.AddReadMessage(BuildMessageAt(7));
		Assert.IsTrue(buffer.Live);
		Assert.AreEqual(1, buffer.BufferCount);
		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(id, messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
	}

	[Test]
	public void skipped_messages_are_not_removed() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddReadMessage(BuildMessageAt(0));
		buffer.AddReadMessage(BuildMessageAt(1));
		buffer.AddReadMessage(BuildMessageAt(2));
		Assert.AreEqual(3, buffer.BufferCount);

		var messagePointer2 = buffer.Scan().Skip(1).First(); // Skip the first message.
		Assert.AreEqual(GetEventIdFor(1), messagePointer2.Message.EventId);
		messagePointer2.MarkSent();
		Assert.AreEqual(2, buffer.BufferCount);

		var messagePointers = buffer.Scan().ToArray();
		Assert.AreEqual(GetEventIdFor(0), messagePointers[0].Message.EventId);
		Assert.AreEqual(GetEventIdFor(2), messagePointers[1].Message.EventId);
	}

	[Test]
	public void retried_messages_appear_first() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddReadMessage(BuildMessageAt(0));
		buffer.AddRetry(BuildMessageAt(2));
		Assert.AreEqual(2, buffer.BufferCount);
		Assert.AreEqual(1, buffer.RetryBufferCount);
		Assert.AreEqual(1, buffer.ReadBufferCount);

		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(2), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(1, buffer.BufferCount);
		Assert.AreEqual(0, buffer.RetryBufferCount);
		Assert.AreEqual(1, buffer.ReadBufferCount);

		messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(0), messagePointer.Message.EventId);
		messagePointer.MarkSent();
		Assert.AreEqual(0, buffer.BufferCount);
		Assert.AreEqual(0, buffer.RetryBufferCount);
		Assert.AreEqual(0, buffer.ReadBufferCount);

		Assert.IsFalse(buffer.Live);
	}

	[Test]
	public void retried_messages_appear_in_version_order() {
		var buffer = new StreamBuffer(10, 10, null, true);
		var id2Retry1 = Guid.NewGuid();
		var id2Retry2 = Guid.NewGuid();
		buffer.AddReadMessage(BuildMessageAt(0));
		buffer.AddRetry(BuildMessageAt(2, id2Retry1));
		buffer.AddRetry(BuildMessageAt(3));
		buffer.AddRetry(BuildMessageAt(1));
		buffer.AddRetry(BuildMessageAt(2, id2Retry2));

		var messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(1), messagePointer.Message.EventId);
		messagePointer.MarkSent();

		messagePointer = buffer.Scan().First();
		Assert.AreEqual(id2Retry1, messagePointer.Message.EventId);
		messagePointer.MarkSent();

		messagePointer = buffer.Scan().First();
		Assert.AreEqual(id2Retry2, messagePointer.Message.EventId);
		messagePointer.MarkSent();

		messagePointer = buffer.Scan().First();
		Assert.AreEqual(GetEventIdFor(3), messagePointer.Message.EventId);
		messagePointer.MarkSent();
	}

	[Test]
	public void lowest_retry_doesnt_assume_order() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddRetry(BuildMessageAt(4));
		buffer.AddRetry(BuildMessageAt(2));
		buffer.AddRetry(BuildMessageAt(3));
		Assert.AreEqual(2, buffer.GetLowestRetry().sequenceNumber);
	}

	[Test]
	public void lowest_retry_ignores_replayed_events() {
		var buffer = new StreamBuffer(10, 10, null, true);
		buffer.AddRetry(BuildMessageAt(4));
		buffer.AddRetry(BuildMessageAt(2));
		buffer.AddRetry(BuildMessageAt(3));
		//add parked events
		buffer.AddRetry(OutstandingMessage.ForParkedEvent(Helper.BuildFakeEvent(Guid.NewGuid(), "foo", "$persistentsubscription-foo::group-parked", 1)));
		Assert.AreEqual(2, buffer.GetLowestRetry().sequenceNumber);
	}

	[Test]
	public void can_add_retries_after_replayed_events() {
		var buffer = new StreamBuffer(10, 10, null, true);
		// add parked events
		var parkedEvent = BuildMessageAt(1);
		buffer.AddRetry(OutstandingMessage.ForParkedEvent(Helper.BuildLinkEvent(Guid.NewGuid(), "$persistentsubscription-foo::group-parked", 0, parkedEvent.ResolvedEvent)));

		// add retried events
		buffer.AddRetry(BuildMessageAt(4));
		buffer.AddRetry(BuildMessageAt(2));
		buffer.AddRetry(BuildMessageAt(3));

		Assert.AreEqual(2, buffer.GetLowestRetry().sequenceNumber);
		var messagePointers = buffer.Scan().ToArray();
		Assert.AreEqual(GetEventIdFor(1), messagePointers[0].Message.ResolvedEvent.Event.EventId);
		Assert.AreEqual(GetEventIdFor(2), messagePointers[1].Message.EventId);
		Assert.AreEqual(GetEventIdFor(3), messagePointers[2].Message.EventId);
		Assert.AreEqual(GetEventIdFor(4), messagePointers[3].Message.EventId);
	}

	private OutstandingMessage BuildMessageAt(int position, Guid? forcedEventId = null) {
		IPersistentSubscriptionStreamPosition previousEventPosition =
			position > 0 ? Helper.GetStreamPositionFor(position - 1, _eventSource) : null;
		var @event = BuildEventAt(position, forcedEventId);
		return OutstandingMessage.ForPushedEvent(
			OutstandingMessage.ForNewEvent(@event, Helper.GetStreamPositionFor(position, _eventSource)), position, previousEventPosition).message;
	}

	private Guid GetEventIdFor(int position) {
		return Helper.GetEventIdFor(position);
	}

	private ResolvedEvent BuildEventAt(int position, Guid? forcedEventId = null) {
		return Helper.GetFakeEventFor(position, _eventSource, forcedEventId);
	}
}
