// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using System.Linq;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Tests.Services.event_reader.multi_stream_reader {
	namespace reordering {
		abstract class with_multi_stream_reader<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
			protected Guid _subscriptionId;
			private QuerySourcesDefinition _sourceDefinition;
			protected IReaderStrategy _readerStrategy;
			protected ReaderSubscriptionOptions _readerSubscriptionOptions;

			protected override bool GivenHeadingReaderRunning() {
				return true;
			}

			protected override void Given() {
				base.Given();
				AllWritesSucceed();
				ExistingEvent("stream-a", "type1", "{}", "{\"Data\": 1}");
				ExistingEvent("stream-b", "type1", "{}", "{\"Data\": 2}");

				GivenOtherEvents();

				_subscriptionId = Guid.NewGuid();
				_sourceDefinition = new QuerySourcesDefinition {
					Streams = new[] {"stream-a", "stream-b"},
					AllEvents = true,
					Options = new QuerySourcesDefinitionOptions {ReorderEvents = true, ProcessingLag = 100}
				};
				_readerStrategy = ReaderStrategy.Create(
					"test",
					0,
					_sourceDefinition,
					_timeProvider,
					stopOnEof: false,
					runAs: null);

				_readerSubscriptionOptions = new ReaderSubscriptionOptions(
					checkpointUnhandledBytesThreshold: 10000, checkpointProcessedEventsThreshold: 100,
					checkpointAfterMs: 10000, stopOnEof: false,
					stopAfterNEvents: null,
					enableContentTypeValidation: false);
			}

			protected abstract void GivenOtherEvents();

			protected string TFPosToMetadata(TFPos tfPos) {
				return string.Format(@"{{""$c"":{0},""$p"":{1}}}", tfPos.CommitPosition, tfPos.PreparePosition);
			}

			[Test]
			public void returns_all_events() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				Assert.AreEqual(5, receivedEvents.Length);
			}

			[Test]
			public void returns_events_in_original_order() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				Assert.That(
					(from e in receivedEvents
						orderby e.Data.Position.PreparePosition
						select e.Data.Position.PreparePosition).SequenceEqual(
						from e in receivedEvents
						select e.Data.Position.PreparePosition), "Incorrect event order received");
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_event_commit_is_delayed<TLogFormat, TStreamId> : with_multi_stream_reader<TLogFormat, TStreamId> {
			protected override void GivenOtherEvents() {
			}

			protected override IEnumerable<WhenStep> When() {
				var fromZeroPosition =
					CheckpointTag.FromStreamPositions(0,
						new Dictionary<string, long> {{"stream-a", -1}, {"stream-b", -1}});
				yield return
					new ReaderSubscriptionManagement.Subscribe(
						_subscriptionId, fromZeroPosition, _readerStrategy, _readerSubscriptionOptions);


				var correlationId = Guid.NewGuid();
				yield return
					new ClientMessage.TransactionStart(
						Guid.NewGuid(), correlationId, GetInputQueue(), true, "stream-a", 0, null);

				var transactionId =
					_consumer.HandledMessages.OfType<ClientMessage.TransactionStartCompleted>()
						.Single(m => m.CorrelationId == correlationId)
						.TransactionId;

				correlationId = Guid.NewGuid();
				yield return
					new ClientMessage.TransactionWrite(
						Guid.NewGuid(), correlationId, GetInputQueue(), true, transactionId,
						new[] {new Event(Guid.NewGuid(), "type1", true, "{Data: 3, Transacted=true}", "{}")}, null);

				correlationId = Guid.NewGuid();
				yield return
					new ClientMessage.WriteEvents(
						Guid.NewGuid(), correlationId, GetInputQueue(), true, "stream-b", 0,
						new[] {new Event(Guid.NewGuid(), "type1", true, "{Data: 4}", "{}")}, null);

				correlationId = Guid.NewGuid();
				yield return
					new ClientMessage.TransactionWrite(
						Guid.NewGuid(), correlationId, GetInputQueue(), true, transactionId,
						new[] {new Event(Guid.NewGuid(), "type1", true, "{Data: 5, Transacted=true}", "{}")}, null);

				correlationId = Guid.NewGuid();
				yield return
					new ClientMessage.TransactionCommit(
						Guid.NewGuid(), correlationId, GetInputQueue(), true, transactionId, null);

				yield return Yield;

				_timeProvider.AddToUtcTime(TimeSpan.FromMilliseconds(300));

				yield return Yield;
			}
		}
	}
}
