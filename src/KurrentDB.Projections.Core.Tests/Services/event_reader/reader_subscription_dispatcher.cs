// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Settings;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using KurrentDB.Projections.Core.Tests.Services.event_reader.heading_event_reader;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class reader_subscription_dispatcher<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
	private ReaderSubscriptionOptions _defaultOptions = new(1000, null, 1000, false, null, false);
	private FakeReaderStrategy _readerStrategy;
	private FakeSubscriptionHandler _handler;
	private Guid _subscriptionId;
	protected override void Given() {
		base.Given();
		_readerStrategy = new FakeReaderStrategy();
		_subscriptionId = Guid.NewGuid();
		_handler = new FakeSubscriptionHandler(_subscriptionId);
	}

	[Test]
	public void should_publish_subscribe_timeout_if_schedule_timeout_is_true() {
		_subscriptionDispatcher.PublishSubscribe(new ReaderSubscriptionManagement.Subscribe(
			_subscriptionId, CheckpointTag.Empty, _readerStrategy, _defaultOptions),
			_handler, scheduleTimeout: true);

		_queue.Process();
		Assert.True(HandledMessages.ContainsSingle<ReaderSubscriptionManagement.Subscribe>());
		Assert.True(HandledMessages.Where(m => m is TimerMessage.Schedule).Any(m =>
			((TimerMessage.Schedule)m).ReplyMessage is EventReaderSubscriptionMessage.SubscribeTimeout));
		_timeProvider.AddToUtcTime(TimeSpan.FromMilliseconds(ESConsts.ReadRequestTimeout * 2));
		_queue.Process();

		Assert.True(_handler.HandledMessages.Any(m => m is EventReaderSubscriptionMessage.SubscribeTimeout));
	}

	[Test]
	public void should_not_publish_subscribe_timeout_if_schedule_timeout_is_false() {
		_subscriptionDispatcher.PublishSubscribe(new ReaderSubscriptionManagement.Subscribe(
				_subscriptionId, CheckpointTag.Empty, _readerStrategy, _defaultOptions),
			_handler, scheduleTimeout: false);

		_queue.Process();
		Assert.True(HandledMessages.ContainsSingle<ReaderSubscriptionManagement.Subscribe>());
		Assert.False(HandledMessages.Where(m => m is TimerMessage.Schedule).Any(m =>
			((TimerMessage.Schedule)m).ReplyMessage is EventReaderSubscriptionMessage.SubscribeTimeout));
	}

	[Test]
	public void should_publish_subscription_messages_to_subscribed_handler() {
		_subscriptionDispatcher.PublishSubscribe(new ReaderSubscriptionManagement.Subscribe(
				_subscriptionId, CheckpointTag.Empty, _readerStrategy, _defaultOptions),
			_handler, scheduleTimeout: false);

		_queue.Process();
		var assignedReaderMessage =
			_handler.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>().FirstOrDefault();
		Assert.NotNull(assignedReaderMessage, "Expected ReaderAssignedReader message to have been published");

		_readerService.Handle(new ReaderSubscriptionMessage.CommittedEventDistributed(
			assignedReaderMessage.ReaderId,
			CreateFakeEvent(), CheckpointTag.Empty));
		_queue.Process();
		var committedEventReceived = _handler.HandledMessages
			.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>()
			.SingleOrDefault();
		Assert.NotNull(committedEventReceived);
	}

	[Test]
	public void should_not_send_subscription_messages_to_cancelled_handlers() {
		_subscriptionDispatcher.PublishSubscribe(new ReaderSubscriptionManagement.Subscribe(
				_subscriptionId, CheckpointTag.Empty, _readerStrategy, _defaultOptions),
			_handler, scheduleTimeout: false);

		_queue.Process();
		var assignedReaderMessage =
			_handler.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>().FirstOrDefault();
		Assert.NotNull(assignedReaderMessage, "Expected ReaderAssignedReader message to have been published");

		_subscriptionDispatcher.Cancel(_subscriptionId);

		_readerService.Handle(new ReaderSubscriptionMessage.CommittedEventDistributed(
			assignedReaderMessage.ReaderId,
			CreateFakeEvent(), CheckpointTag.Empty));
		_queue.Process();

		Assert.IsEmpty(_handler.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>());
	}

	[Test]
	public void should_send_subscription_messages_to_handler_that_has_subscribed_without_publishing() {
		_subscriptionDispatcher.Subscribed(_subscriptionId, _handler);

		_readerService.Handle(new ReaderSubscriptionManagement.Subscribe(
			_subscriptionId, CheckpointTag.Empty, _readerStrategy, _defaultOptions));

		_queue.Process();
		Assert.IsNotEmpty(_handler.HandledMessages
			.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>());
	}


	[Test]
	public void multiple_handlers_should_only_receive_messages_for_their_subscription() {
		var secondSubscription = Guid.NewGuid();
		var secondHandler = new FakeSubscriptionHandler(secondSubscription);
		_subscriptionDispatcher.PublishSubscribe(new ReaderSubscriptionManagement.Subscribe(
				_subscriptionId, CheckpointTag.Empty, _readerStrategy, _defaultOptions),
			_handler, scheduleTimeout: false);
		_subscriptionDispatcher.PublishSubscribe(new ReaderSubscriptionManagement.Subscribe(
				secondSubscription, CheckpointTag.Empty, _readerStrategy, _defaultOptions),
			secondHandler, scheduleTimeout: false);

		_queue.Process();
		Assert.True(_handler.HandledMessages.All(x => x.SubscriptionId == _subscriptionId));
		Assert.True(secondHandler.HandledMessages.All(x => x.SubscriptionId == secondSubscription));
		Assert.AreEqual(1, _handler.HandledMessages.Count(x => x is EventReaderSubscriptionMessage.ReaderAssignedReader));
		Assert.AreEqual(1, secondHandler.HandledMessages.Count(x => x is EventReaderSubscriptionMessage.ReaderAssignedReader));
	}

	private ResolvedEvent CreateFakeEvent() => new(
		"test-stream", 0, "test-stream", 0, false, new TFPos(100, 100),
		Guid.NewGuid(), "test-event", isJson: true,
		"{\"foo\":\"bar\"}", "{}");

	private class FakeSubscriptionHandler : IHandle<EventReaderSubscriptionMessage.ProgressChanged>,
		IHandle<EventReaderSubscriptionMessage.CommittedEventReceived>,
		IHandle<EventReaderSubscriptionMessage.SubscriptionStarted>,
		IHandle<EventReaderSubscriptionMessage.NotAuthorized>,
		IHandle<EventReaderSubscriptionMessage.EofReached>,
		IHandle<EventReaderSubscriptionMessage.CheckpointSuggested>,
		IHandle<EventReaderSubscriptionMessage.ReaderAssignedReader>,
		IHandle<EventReaderSubscriptionMessage.Failed>,
		IHandle<EventReaderSubscriptionMessage.SubscribeTimeout> {
		public Guid SubscriptionId { get; }

		public FakeSubscriptionHandler(Guid subscriptionId) {
			SubscriptionId = subscriptionId;
		}

		public List<EventReaderSubscriptionMessageBase> HandledMessages = new();
		public void Handle(EventReaderSubscriptionMessage.ProgressChanged message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.SubscriptionStarted message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.NotAuthorized message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.EofReached message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.CheckpointSuggested message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.ReaderAssignedReader message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.Failed message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.SubscribeTimeout message) => HandledMessages.Add(message);
		public void Handle(EventReaderSubscriptionMessage.CommittedEventReceived message) =>
			HandledMessages.Add(message);
	}
}
