// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Services.PersistentSubscription;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
public class OutstandingMessageCacheTests {
	private EventSource _eventSource;
	public OutstandingMessageCacheTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	private OutstandingMessage BuildMessageAt(int eventPosition, EventSource eventSource, Guid? forcedEventId = null) {
		IPersistentSubscriptionStreamPosition previousEventPosition =
			eventPosition > 0 ? GetStreamPositionFor(eventPosition - 1) : null;
		var @event = Helper.GetFakeEventFor(eventPosition, eventSource, forcedEventId);
		return OutstandingMessage.ForPushedEvent(
			OutstandingMessage.ForNewEvent(@event, GetStreamPositionFor(eventPosition)),
			eventPosition, previousEventPosition).message;
	}

	private Guid GetEventIdFor(int position) {
		return Helper.GetEventIdFor(position);
	}

	private IPersistentSubscriptionStreamPosition GetStreamPositionFor(int position) {
		return Helper.GetStreamPositionFor(position, _eventSource);
	}

	[Test]
	public void when_created_has_zero_count() {
		var cache = new OutstandingMessageCache();
		Assert.AreEqual(0, cache.Count);
	}

	[Test]
	public void can_remove_non_existing_item() {
		var cache = new OutstandingMessageCache();
		Assert.DoesNotThrow(() => cache.Remove(Guid.NewGuid()));
	}

	[Test]
	public void adding_an_item_causes_count_to_go_up() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(0, _eventSource), DateTime.Now);
		Assert.AreEqual(1, cache.Count);
		Assert.AreEqual(GetStreamPositionFor(0), cache.GetLowestPosition().message?.EventPosition);
	}

	[Test]
	public void can_add_duplicate() {
		var cache = new OutstandingMessageCache();
		var id = Guid.NewGuid();
		var result1 =
			cache.StartMessage(BuildMessageAt(0, _eventSource, id), DateTime.Now);
		var result2 =
			cache.StartMessage(BuildMessageAt(1, _eventSource, id), DateTime.Now);
		Assert.AreEqual(1, cache.Count);
		Assert.AreEqual(GetStreamPositionFor(0), cache.GetLowestPosition().message?.EventPosition);
		Assert.AreEqual(StartMessageResult.Success, result1);
		Assert.AreEqual(StartMessageResult.SkippedDuplicate, result2);
	}

	[Test]
	public void can_remove_duplicate() {
		var cache = new OutstandingMessageCache();
		var id = Guid.NewGuid();
		cache.StartMessage(BuildMessageAt(0, _eventSource, id), DateTime.Now);
		cache.StartMessage(BuildMessageAt(1, _eventSource, id), DateTime.Now);
		cache.Remove(id);
		Assert.AreEqual(0, cache.Count);
		Assert.IsNull(cache.GetLowestPosition().message);
		Assert.AreEqual(long.MaxValue, cache.GetLowestPosition().sequenceNumber);
	}

	[Test]
	public void can_remove_existing_item() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(0, _eventSource), DateTime.Now);
		cache.Remove(GetEventIdFor(0));
		Assert.AreEqual(0, cache.Count);
		Assert.IsNull(cache.GetLowestPosition().message);
		Assert.AreEqual(long.MaxValue, cache.GetLowestPosition().sequenceNumber);
	}

	[Test]
	public void lowest_works_on_add() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(9, _eventSource), DateTime.Now);
		Assert.AreEqual(GetStreamPositionFor(9), cache.GetLowestPosition().message?.EventPosition);
	}

	[Test]
	public void lowest_works_on_adds_then_remove() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(7, _eventSource), DateTime.Now);
		cache.StartMessage(BuildMessageAt(8, _eventSource), DateTime.Now);
		cache.StartMessage(BuildMessageAt(9, _eventSource), DateTime.Now);
		cache.Remove(GetEventIdFor(7));
		Assert.AreEqual(GetStreamPositionFor(8), cache.GetLowestPosition().message?.EventPosition);
	}

	[Test]
	public void lowest_on_empty_cache_returns_max() {
		var cache = new OutstandingMessageCache();
		Assert.IsNull(cache.GetLowestPosition().message);
		Assert.AreEqual(long.MaxValue, cache.GetLowestPosition().sequenceNumber);
	}

	[Test]
	public void lowest_ignores_replayed_events() {
		var cache = new OutstandingMessageCache();
		//normal event:
		cache.StartMessage(BuildMessageAt(5, _eventSource), DateTime.Now);
		//replayed event:
		cache.StartMessage(OutstandingMessage.ForParkedEvent(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "$persistentsubscription-name::group-parked", 4)), DateTime.Now);
		Assert.AreEqual(GetStreamPositionFor(5), cache.GetLowestPosition().message?.EventPosition);
	}

	[Test]
	public void get_expired_messages_returns_max_value_on_empty_cache() {
		var cache = new OutstandingMessageCache();
		Assert.AreEqual(0, cache.GetMessagesExpiringBefore(DateTime.Now).Count());
		Assert.IsNull(cache.GetLowestPosition().message);
		Assert.AreEqual(long.MaxValue, cache.GetLowestPosition().sequenceNumber);
	}

	[Test]
	public void message_that_expires_is_included_in_expired_list() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(0, _eventSource),
			DateTime.Now.AddSeconds(-1));
		var expired = cache.GetMessagesExpiringBefore(DateTime.Now).ToList();
		Assert.AreEqual(1, expired.Count());
		Assert.AreEqual(GetEventIdFor(0), expired.FirstOrDefault().EventId);
	}

	[Test]
	public void message_that_expires_is_included_in_expired_list_with_another_that_should_not() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(0, _eventSource), DateTime.Now.AddSeconds(-1));
		cache.StartMessage(
			BuildMessageAt(1, _eventSource), DateTime.Now.AddSeconds(1));
		var expired = cache.GetMessagesExpiringBefore(DateTime.Now).ToList();
		Assert.AreEqual(1, expired.Count());
		Assert.AreEqual(GetEventIdFor(0), expired.FirstOrDefault().EventId);
	}

	[Test]
	public void message_that_is_removed_does_not_show_up_in_expired_list() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(1, _eventSource),
			DateTime.Now.AddSeconds(-11));
		cache.Remove(GetEventIdFor(1));
		var expired = cache.WaitingTimeMessages();
		Assert.AreEqual(0, expired.Count());
	}

	[Test]
	public void can_remove_non_first_message_and_have_removed_from_time() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(
			BuildMessageAt(1, _eventSource),
			DateTime.Now.AddSeconds(-12));
		cache.StartMessage(
			BuildMessageAt(2, _eventSource),
			DateTime.Now.AddSeconds(-11));
		cache.Remove(GetEventIdFor(2));
		var expired = cache.WaitingTimeMessages();
		Assert.AreEqual(1, expired.Count());
		Assert.AreEqual(GetEventIdFor(1), expired.FirstOrDefault().Item2.MessageId);
	}

	[Test]
	public void can_add_multiple_messages_same_time_different_ids() {
		var cache = new OutstandingMessageCache();
		var time = DateTime.Now.AddSeconds(-12);
		cache.StartMessage(
			BuildMessageAt(1, _eventSource), time);
		cache.StartMessage(
			BuildMessageAt(2, _eventSource), time);
		var expired = cache.WaitingTimeMessages();
		Assert.AreEqual(2, expired.Count());
	}

	[Test]
	public void can_remove_second_message_same_time_different_ids() {
		var cache = new OutstandingMessageCache();
		var time = DateTime.Now.AddSeconds(-12);
		cache.StartMessage(
			BuildMessageAt(1, _eventSource), time);
		cache.StartMessage(
			BuildMessageAt(2, _eventSource), time);
		cache.Remove(GetEventIdFor(2));
		var expired = cache.WaitingTimeMessages();
		Assert.AreEqual(GetEventIdFor(1), expired.FirstOrDefault().Item2.MessageId);
		Assert.AreEqual(1, expired.Count());
	}

	[Test]
	public void message_that_notexpired_is_not_included_in_expired_list() {
		var cache = new OutstandingMessageCache();
		cache.StartMessage(BuildMessageAt(0, _eventSource),
			DateTime.Now.AddSeconds(1));
		var expired = cache.GetMessagesExpiringBefore(DateTime.Now).ToList();
		Assert.AreEqual(0, expired.Count());
	}
}
