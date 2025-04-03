// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Projections.Core.EventReaders.Feeds;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messages.EventReaders.Feeds;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using System.Linq;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.feed_reader {
	namespace feed_reader {
		[TestFixture]
		public class when_creating {
			private IPublisher _bus;

			private ReaderSubscriptionDispatcher _subscriptionDispatcher;

			private QuerySourcesDefinition _testQueryDefinition;

			[SetUp]
			public void SetUp() {
				_bus = new SynchronousScheduler();
				_subscriptionDispatcher = new ReaderSubscriptionDispatcher(_bus);
				_testQueryDefinition = new QuerySourcesDefinition {AllStreams = true, AllEvents = true};
			}

			[Test]
			public void it_can_be_created() {
				new FeedReader(
					_subscriptionDispatcher, SystemAccounts.System, _testQueryDefinition,
					CheckpointTag.FromPosition(0, 0, -1), 10, Guid.NewGuid(), new NoopEnvelope(),
					new RealTimeProvider());
			}

			[Test]
			public void null_subscription_dispatcher_throws_argument_null_exception() {
				Assert.Throws<ArgumentNullException>(() => {
					new FeedReader(
						null, SystemAccounts.System, _testQueryDefinition, CheckpointTag.FromPosition(0, 0, -1), 10,
						Guid.NewGuid(), new NoopEnvelope(), new RealTimeProvider());
				});
			}

			[Test]
			public void null_user_account_is_allowed() {
				new FeedReader(
					_subscriptionDispatcher, null, _testQueryDefinition, CheckpointTag.FromPosition(0, 0, -1), 10,
					Guid.NewGuid(), new NoopEnvelope(), new RealTimeProvider());
			}

			[Test]
			public void null_query_definition_throws_argument_null_exception() {
				Assert.Throws<ArgumentNullException>(() => {
					new FeedReader(
						_subscriptionDispatcher, SystemAccounts.System, null, CheckpointTag.FromPosition(0, 0, -1),
						10, Guid.NewGuid(),
						new NoopEnvelope(), new RealTimeProvider());
				});
			}

			[Test]
			public void null_from_position_throws_argument_null_exception() {
				Assert.Throws<ArgumentNullException>(() => {
					new FeedReader(
						_subscriptionDispatcher, SystemAccounts.System, _testQueryDefinition, null, 10,
						Guid.NewGuid(),
						new NoopEnvelope(), new RealTimeProvider());
				});
			}

			[Test]
			public void null_envelope_throws_argument_null_exception() {
				Assert.Throws<ArgumentNullException>(() => {
					new FeedReader(
						_subscriptionDispatcher, SystemAccounts.System, _testQueryDefinition,
						CheckpointTag.FromPosition(0, 0, -1), 10, Guid.NewGuid(), null, new RealTimeProvider());
				});
			}

			[Test]
			public void zero_max_events_throws_argument_exception() {
				Assert.Throws<ArgumentException>(() => {
					new FeedReader(
						_subscriptionDispatcher, SystemAccounts.System, _testQueryDefinition,
						CheckpointTag.FromPosition(0, 0, -1), 0, Guid.NewGuid(), new NoopEnvelope(),
						new RealTimeProvider());
				});
			}

			[Test]
			public void negative_max_events_throws_argument_exception() {
				Assert.Throws<ArgumentException>(() => {
					new FeedReader(
						_subscriptionDispatcher, SystemAccounts.System, _testQueryDefinition,
						CheckpointTag.FromPosition(0, 0, -1), -1, Guid.NewGuid(), new NoopEnvelope(),
						new RealTimeProvider());
				});
			}
		}

		public abstract class FeedReaderSpecification {
			protected SynchronousScheduler _bus;

			protected ReaderSubscriptionDispatcher _subscriptionDispatcher;

			protected QuerySourcesDefinition _testQueryDefinition;
			protected TestHandler<Message> _consumer;
			protected FeedReader _feedReader;

			[SetUp]
			public void SetUp() {
				_bus = new();
				_consumer = new TestHandler<Message>();
				_bus.Subscribe(_consumer);
				_subscriptionDispatcher = new ReaderSubscriptionDispatcher(_bus);
				_testQueryDefinition = GivenQuerySource();
				_feedReader = new FeedReader(
					_subscriptionDispatcher, SystemAccounts.System, _testQueryDefinition, GivenFromPosition(), 10,
					Guid.NewGuid(), _bus, new RealTimeProvider());
				Given();
				When();
			}

			protected virtual void Given() {
			}

			protected abstract void When();

			protected virtual CheckpointTag GivenFromPosition() {
				return CheckpointTag.FromPosition(0, 0, -1);
			}

			protected abstract QuerySourcesDefinition GivenQuerySource();
		}

		[TestFixture]
		public class when_starting : FeedReaderSpecification {
			protected override QuerySourcesDefinition GivenQuerySource() {
				return new QuerySourcesDefinition {AllStreams = true, AllEvents = true};
			}

			protected override void When() {
				_feedReader.Start();
			}

			[Test]
			public void publishes_subscribe_message() {
				var subscribe = _consumer.HandledMessages.OfType<ReaderSubscriptionManagement.Subscribe>().ToArray();
				Assert.AreEqual(1, subscribe.Length);
			}

			[Test]
			public void subscribes_to_a_finite_number_of_events() {
				var subscribe = _consumer.HandledMessages.OfType<ReaderSubscriptionManagement.Subscribe>().Single();
				Assert.NotNull(subscribe.Options);
				Assert.NotNull(subscribe.Options.StopAfterNEvents);
				Assert.Less(0, subscribe.Options.StopAfterNEvents);
				Assert.IsTrue(subscribe.Options.StopOnEof);
			}
		}

		[TestFixture]
		public class when_handling_committed_events : FeedReaderSpecification {
			private Guid _subscriptionId;
			private int _number;

			protected override QuerySourcesDefinition GivenQuerySource() {
				return new QuerySourcesDefinition {AllStreams = true, AllEvents = true};
			}

			protected override void Given() {
				base.Given();
				_feedReader.Start();
				var subscribe = _consumer.HandledMessages.OfType<ReaderSubscriptionManagement.Subscribe>().Single();
				_subscriptionId = subscribe.SubscriptionId;
				_number = 0;
			}

			protected override void When() {
				for (var i = 0; i < 100; i++) {
					_feedReader.Handle(
						EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
							new ResolvedEvent(
								"stream", i, "stream", i, false, new TFPos(i * 100, i * 100 - 50),
								new TFPos(i * 100, i * 100 - 50), Guid.NewGuid(), "type", false, new byte[0],
								new byte[0],
								new byte[0], null, DateTime.UtcNow), _subscriptionId, _number++));
				}
			}

			[Test]
			public void does_not_publish_feed_page_message() {
				var feedPageMessages = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().ToArray();
				Assert.AreEqual(0, feedPageMessages.Length);
			}
		}

		[TestFixture]
		public class when_handling_eof_message : FeedReaderSpecification {
			private Guid _subscriptionId;
			private int _number;
			private int _maxN;

			protected override QuerySourcesDefinition GivenQuerySource() {
				return new QuerySourcesDefinition {AllStreams = true, AllEvents = true};
			}

			protected override void Given() {
				base.Given();
				_feedReader.Start();
				var subscribe = _consumer.HandledMessages.OfType<ReaderSubscriptionManagement.Subscribe>().Single();
				_subscriptionId = subscribe.SubscriptionId;
				_maxN = (int)subscribe.Options.StopAfterNEvents;
				_number = 0;
				for (var i = 0; i < _maxN; i++) {
					_feedReader.Handle(
						EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
							new ResolvedEvent(
								"stream", i, "stream", i, false, new TFPos(i * 100, i * 100 - 50),
								new TFPos(i * 100, i * 100 - 50), Guid.NewGuid(), "type", false, new byte[0],
								new byte[0],
								new byte[0], null, DateTime.UtcNow), _subscriptionId, _number++));
				}
			}

			protected override void When() {
				_feedReader.Handle(
					new EventReaderSubscriptionMessage.EofReached(
						_subscriptionId, CheckpointTag.FromPosition(0, _maxN * 100, _maxN * 100 - 50), _number++));
			}

			[Test]
			public void publishes_feed_page_message() {
				var feedPageMessages = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().ToArray();
				Assert.AreEqual(1, feedPageMessages.Length);
			}

			[Test]
			public void publishes_feed_page_with_all_received_events() {
				var feedPageMessage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().Single();
				Assert.AreEqual(_maxN, feedPageMessage.Events.Length);
			}

			[Test]
			public void publishes_feed_page_with_events_in_correct_order() {
				var feedPageMessage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().Single();
				for (var i = 0; i < feedPageMessage.Events.Length - 1; i++) {
					var first = feedPageMessage.Events[i];
					var second = feedPageMessage.Events[i + 1];

					Assert.That(first.ReaderPosition < second.ReaderPosition);
					Assert.That(first.ResolvedEvent.Position < second.ResolvedEvent.Position);
				}
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_reading_existing_events<TLogFormat, TStreamId> : TestFixtureWithFeedReaderService<TLogFormat, TStreamId> {
			private QuerySourcesDefinition _querySourcesDefinition;
			private CheckpointTag _fromPosition;
			private int _maxEvents;

			protected override void Given() {
				base.Given();
				ExistingEvent("test-stream", "type1", "{}", "{Data: 1}");
				ExistingEvent("test-stream", "type1", "{}", "{Data: 2}");
				ExistingEvent("test-stream", "type2", "{}", "{Data: 3}");

				_querySourcesDefinition = new QuerySourcesDefinition
					{Streams = new[] {"test-stream"}, AllEvents = true};
				_fromPosition = CheckpointTag.FromStreamPosition(0, "test-stream", -1);
				_maxEvents = 2;
			}

			protected override IEnumerable<WhenStep> When() {
				yield return
					new FeedReaderMessage.ReadPage(
						Guid.NewGuid(), GetInputQueue(), SystemAccounts.System,
						_querySourcesDefinition, _fromPosition, _maxEvents);
			}

			[Test]
			public void publishes_feed_page_message() {
				var feedPage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().ToArray();
				Assert.AreEqual(1, feedPage.Length);
			}

			[Test]
			public void returns_correct_last_reader_position() {
				var feedPage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().Single();
				Assert.AreEqual(CheckpointTag.FromStreamPosition(0, "test-stream", 1), feedPage.LastReaderPosition);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_reading_existing_events_by_parts<TLogFormat, TStreamId> : TestFixtureWithFeedReaderService<TLogFormat, TStreamId> {
			private QuerySourcesDefinition _querySourcesDefinition;
			private CheckpointTag _fromPosition;
			private int _maxEvents;

			protected override void Given() {
				base.Given();
				ExistingEvent("test-stream", "type1", "{}", "{Data: 1}");
				ExistingEvent("test-stream", "type1", "{}", "{Data: 2}");
				ExistingEvent("test-stream", "type2", "{}", "{Data: 3}");

				_querySourcesDefinition = new QuerySourcesDefinition
					{Streams = new[] {"test-stream"}, AllEvents = true};
				_fromPosition = CheckpointTag.FromStreamPosition(0, "test-stream", -1);
				_maxEvents = 2;
			}

			protected override IEnumerable<WhenStep> When() {
				yield return
					new FeedReaderMessage.ReadPage(
						Guid.NewGuid(), GetInputQueue(), SystemAccounts.System,
						_querySourcesDefinition, _fromPosition, _maxEvents);
				var feedPage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().Single();
				yield return
					new FeedReaderMessage.ReadPage(
						Guid.NewGuid(), GetInputQueue(), SystemAccounts.System,
						_querySourcesDefinition, feedPage.LastReaderPosition, _maxEvents);
			}

			[Test]
			public void publishes_feed_page_message() {
				var feedPage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().ToArray();
				Assert.AreEqual(2, feedPage.Length);
			}

			[Test]
			public void returns_correct_last_reader_position() {
				var feedPage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().Last();
				Assert.AreEqual(CheckpointTag.FromStreamPosition(0, "test-stream", 2), feedPage.LastReaderPosition);
			}
		}
	}
}
