// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using KurrentDB.Projections.Core.Tests.Services.event_reader.heading_event_reader;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.event_reader_core_service;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_subscribe_requests<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
	private readonly ReaderSubscriptionOptions _defaultOptions = new(1000, 10, 1000, false, null, false);

	[Test]
	public void should_publish_subscription_failed_if_the_reader_is_not_running() {
		EventReaderSubscriptionMessage.Failed failedMessage = null;
		var subscriptionId = Guid.NewGuid();
		_readerService.Handle(new ReaderCoreServiceMessage.StopReader(Guid.Empty));
		SubscriptionDispatcher.PublishSubscribe(
			new(subscriptionId, CheckpointTag.Empty, new FakeReaderStrategy(), _defaultOptions),
			new AdHocHandlerStruct<EventReaderSubscriptionMessage.Failed>(m => failedMessage = m, null),
			scheduleTimeout: false);
		_queue.Process();

		Assert.NotNull(failedMessage, $"Expected {nameof(ReaderSubscriptionDispatcher)} to publish a {nameof(EventReaderSubscriptionMessage.Failed)} message");
		Assert.AreEqual(subscriptionId, failedMessage.SubscriptionId);
		Assert.AreEqual($"{nameof(EventReaderCoreService)} is stopped", failedMessage.Reason);
	}

	[Test]
	public void should_publish_subscription_failed_if_creating_the_reader_fails() {
		EventReaderSubscriptionMessage.Failed failedMessage = null;
		var subscriptionId = Guid.NewGuid();
		SubscriptionDispatcher.PublishSubscribe(
			new(subscriptionId, CheckpointTag.Empty, FakeReaderStrategyThatThrows.ThrowOnCreateReaderSubscription(), _defaultOptions),
			new AdHocHandlerStruct<EventReaderSubscriptionMessage.Failed>(m => failedMessage = m, null),
			scheduleTimeout: false);
		_queue.Process();

		Assert.NotNull(failedMessage, $"Expected {nameof(ReaderSubscriptionDispatcher)} to publish a {nameof(EventReaderSubscriptionMessage.Failed)} message");
		Assert.AreEqual(subscriptionId, failedMessage.SubscriptionId);
		Assert.True(failedMessage.Reason.Contains(nameof(FakeReaderStrategyThatThrows)));
	}

	[Test]
	public void should_publish_subscription_failed_if_creating_the_paused_event_reader_fails() {
		EventReaderSubscriptionMessage.Failed failedMessage = null;
		var subscriptionId = Guid.NewGuid();
		SubscriptionDispatcher.PublishSubscribe(
			new(subscriptionId, CheckpointTag.Empty, FakeReaderStrategyThatThrows.ThrowOnCreatePausedReader(), _defaultOptions),
			new AdHocHandlerStruct<EventReaderSubscriptionMessage.Failed>(m => failedMessage = m, null),
			scheduleTimeout: false);
		_queue.Process();

		Assert.NotNull(failedMessage, $"Expected {nameof(ReaderSubscriptionDispatcher)} to publish a {nameof(EventReaderSubscriptionMessage.Failed)} message");
		Assert.AreEqual(subscriptionId, failedMessage.SubscriptionId);
		Assert.True(failedMessage.Reason.Contains(nameof(FakeReaderSubscriptionThatThrows)));
	}

	private class FakeReaderStrategyThatThrows : IReaderStrategy {
		private readonly bool _throwOnCreateSubscription;
		private readonly bool _throwOnCreatePausedReader;

		private FakeReaderStrategyThatThrows(bool throwOnCreateSubscription, bool throwOnCreatePausedReader) {
			_throwOnCreateSubscription = throwOnCreateSubscription;
			_throwOnCreatePausedReader = throwOnCreatePausedReader;
		}

		public static FakeReaderStrategyThatThrows ThrowOnCreateReaderSubscription() => new(true, false);
		public static FakeReaderStrategyThatThrows ThrowOnCreatePausedReader() => new(false, true);

		public bool IsReadingOrderRepeatable { get; }
		public EventFilter EventFilter { get; }
		public PositionTagger PositionTagger { get; }

		public IReaderSubscription CreateReaderSubscription(IPublisher publisher,
			CheckpointTag fromCheckpointTag,
			Guid subscriptionId,
			ReaderSubscriptionOptions readerSubscriptionOptions) {
			if (_throwOnCreateSubscription)
				throw new ArgumentException(nameof(FakeReaderStrategyThatThrows));
			if (_throwOnCreatePausedReader)
				return new FakeReaderSubscriptionThatThrows();
			return new FakeReaderSubscription();
		}

		public IEventReader CreatePausedEventReader(Guid eventReaderId,
			IPublisher publisher,
			CheckpointTag checkpointTag,
			bool stopOnEof) {
			throw new NotImplementedException();
		}
	}

	private class FakeReaderSubscriptionThatThrows : IReaderSubscription {
		public void Handle(ReaderSubscriptionMessage.CommittedEventDistributed message) {
			throw new NotImplementedException();
		}

		public void Handle(ReaderSubscriptionMessage.EventReaderIdle message) {
			throw new NotImplementedException();
		}

		public void Handle(ReaderSubscriptionMessage.EventReaderStarting message) {
			throw new NotImplementedException();
		}

		public void Handle(ReaderSubscriptionMessage.EventReaderEof message) {
			throw new NotImplementedException();
		}

		public void Handle(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
			throw new NotImplementedException();
		}

		public void Handle(ReaderSubscriptionMessage.EventReaderNotAuthorized message) {
			throw new NotImplementedException();
		}

		public void Handle(ReaderSubscriptionMessage.ReportProgress message) {
			throw new NotImplementedException();
		}

		public string Tag { get; }
		public Guid SubscriptionId { get; }

		public IEventReader CreatePausedEventReader(IPublisher publisher, IODispatcher ioDispatcher, Guid forkedEventReaderId) {
			throw new ArgumentException(nameof(FakeReaderSubscriptionThatThrows));
		}
	}
}
