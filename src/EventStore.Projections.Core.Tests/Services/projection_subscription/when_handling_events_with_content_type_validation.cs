// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Subscriptions;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projection_subscription;

public class when_handling_events_with_content_type_validation {
	[TestFixture]
	public class given_json_events_and_content_type_validation_enabled : TestFixtureWithProjectionSubscription {
		protected override void When() {
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(100, 100), "test-stream", 0, false, Guid.NewGuid(),
					"null-json", true, null, null));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(200, 200), "test-stream", 1, false, Guid.NewGuid(),
					"invalid-json", true, Encoding.UTF8.GetBytes("{foo:bar}"), new byte[0]));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(300, 300), "test-stream", 2, false, Guid.NewGuid(),
					"valid-json", true, Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}"), new byte[0]));
		}

		protected override IReaderSubscription CreateProjectionSubscription() {
			return new ReaderSubscription(
				"Test Subscription",
				_bus,
				_projectionCorrelationId,
				_readerStrategy.PositionTagger.MakeZeroCheckpointTag(),
				_readerStrategy,
				_timeProvider,
				_checkpointUnhandledBytesThreshold,
				_checkpointProcessedEventsThreshold,
				_checkpointAfterMs,
				false,
				null,
				true);
		}

		[Test]
		public void only_the_valid_json_is_handled() {
			Assert.AreEqual(1, _eventHandler.HandledMessages.Count);
			Assert.AreEqual("valid-json", _eventHandler.HandledMessages[0].Data.EventType);
		}
	}

	[TestFixture]
	public class given_non_json_events_and_content_type_validation_enabled : TestFixtureWithProjectionSubscription {
		protected override void When() {
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(100, 100), "test-stream", 0, false, Guid.NewGuid(),
					"null-event", false, null, null));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(200, 200), "test-stream", 1, false, Guid.NewGuid(),
					"plain-text", false, Encoding.UTF8.GetBytes("foo bar"), new byte[0]));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(300, 300), "test-stream", 2, false, Guid.NewGuid(),
					"valid-json", false, Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}"), new byte[0]));
		}

		protected override IReaderSubscription CreateProjectionSubscription() {
			return new ReaderSubscription(
				"Test Subscription",
				_bus,
				_projectionCorrelationId,
				_readerStrategy.PositionTagger.MakeZeroCheckpointTag(),
				_readerStrategy,
				_timeProvider,
				_checkpointUnhandledBytesThreshold,
				_checkpointProcessedEventsThreshold,
				_checkpointAfterMs,
				false,
				null,
				true);
		}

		[Test]
		public void all_events_are_handled() {
			Assert.AreEqual(3, _eventHandler.HandledMessages.Count);
		}
	}

	[TestFixture]
	public class given_json_and_non_json_events_and_content_type_validation_disabled : TestFixtureWithProjectionSubscription {
		protected override void When() {
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(100, 100), "test-stream", 0, false, Guid.NewGuid(),
					"null-json", true, null, null));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(200, 200), "test-stream", 1, false, Guid.NewGuid(),
					"invalid-json", true, Encoding.UTF8.GetBytes("{foo:bar}"), new byte[0]));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(300, 300), "test-stream", 2, false, Guid.NewGuid(),
					"valid-json", true, Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}"), new byte[0]));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(400, 400), "test-stream", 3, false, Guid.NewGuid(),
					"null-event", false, null, null));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(500, 500), "test-stream", 4, false, Guid.NewGuid(),
					"plain-text", false, Encoding.UTF8.GetBytes("foo bar"), new byte[0]));
			_subscription.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(600, 600), "test-stream", 5, false, Guid.NewGuid(),
					"valid-json", false, Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}"), new byte[0]));
		}

		protected override IReaderSubscription CreateProjectionSubscription() {
			return new ReaderSubscription(
				"Test Subscription",
				_bus,
				_projectionCorrelationId,
				_readerStrategy.PositionTagger.MakeZeroCheckpointTag(),
				_readerStrategy,
				_timeProvider,
				_checkpointUnhandledBytesThreshold,
				_checkpointProcessedEventsThreshold,
				_checkpointAfterMs,
				false,
				null,
				false);
		}

		[Test]
		public void all_events_are_handled() {
			Assert.AreEqual(6, _eventHandler.HandledMessages.Count);
		}
	}
}
