// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_reader.event_by_type_index_reader.catching_up {
	namespace when_reordering_happens_in_event_by_type_index {
		abstract class ReadingReorderedEventsInTheIndexTestFixture<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
			protected Guid _subscriptionId;
			private QuerySourcesDefinition _sourceDefinition;
			protected IReaderStrategy _readerStrategy;
			protected ReaderSubscriptionOptions _readerSubscriptionOptions;
			protected TFPos _tfPos1;
			protected TFPos _tfPos2;
			protected TFPos _tfPos3;

			protected override bool GivenHeadingReaderRunning() {
				return true;
			}

			protected override void Given() {
				base.Given();
				AllWritesSucceed();

				_tfPos1 = ExistingEvent("test-stream", "type1", "{}", "{Data: 1}");
				_tfPos2 = ExistingEvent("test-stream", "type1", "{}", "{Data: 2}");
				_tfPos3 = ExistingEvent("test-stream", "type2", "{}", "{Data: 3}");

				GivenInitialIndexState();

				_subscriptionId = Guid.NewGuid();
				_sourceDefinition = new QuerySourcesDefinition {
					AllStreams = true,
					Events = new[] {"type1", "type2"},
					Options = new QuerySourcesDefinitionOptions { }
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
					enableContentTypeValidation: true);
			}

			protected abstract void GivenInitialIndexState();

			protected string TFPosToMetadata(TFPos tfPos) {
				return string.Format(@"{{""$c"":{0},""$p"":{1}}}", tfPos.CommitPosition, tfPos.PreparePosition);
			}

			[Test]
			public void returns_all_events() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				Assert.AreEqual(3, receivedEvents.Length);
			}

			[Test]
			public void returns_events_in_original_order() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				Assert.That(
					new long[] {0, 1, 2}.SequenceEqual(from e in receivedEvents
						select e.Data.EventSequenceNumber),
					"Incorrect event order received");
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_starting_with_empty_index<TLogFormat, TStreamId> : ReadingReorderedEventsInTheIndexTestFixture<TLogFormat, TStreamId> {
			protected override void GivenInitialIndexState() {
				NoStream("$et-type1");
				NoStream("$et-type2");
				NoStream("$et");
			}

			protected override IEnumerable<WhenStep> When() {
				var fromZeroPosition = CheckpointTag.FromEventTypeIndexPositions(
					0, new TFPos(0, -1), new Dictionary<string, long> {{"type1", -1}, {"type2", -1}});
				yield return
					new ReaderSubscriptionManagement.Subscribe(
						_subscriptionId, fromZeroPosition, _readerStrategy, _readerSubscriptionOptions);

				// simulate index-by-type system projection
				yield return
					new ClientMessage.WriteEvents(
						Guid.NewGuid(), Guid.NewGuid(), new NoopEnvelope(), false, "$et-type1", ExpectedVersion.Any,
						new Event(Guid.NewGuid(), "$>", false, "0@test-stream", TFPosToMetadata(_tfPos1)), user: null);

				// simulate index-by-type system projection (the second event write is delayed - awaiting for ACK from the previous write)
				yield return
					new ClientMessage.WriteEvents(
						Guid.NewGuid(), Guid.NewGuid(), new NoopEnvelope(), false, "$et-type2", ExpectedVersion.Any,
						new Event(Guid.NewGuid(), "$>", false, "2@test-stream", TFPosToMetadata(_tfPos3)), user: null);

				// simulate index-by-type system projection (ACK received - writing the next event)
				yield return
					new ClientMessage.WriteEvents(
						Guid.NewGuid(), Guid.NewGuid(), new NoopEnvelope(), false, "$et-type1", ExpectedVersion.Any,
						new Event(Guid.NewGuid(), "$>", false, "1@test-stream", TFPosToMetadata(_tfPos2)), user: null);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_starting_with_partially_built_index<TLogFormat, TStreamId> : ReadingReorderedEventsInTheIndexTestFixture<TLogFormat, TStreamId> {
			protected override void GivenInitialIndexState() {
				// simulate index-by-type system projection
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos1), "0@test-stream");

				// simulate index-by-type system projection (the second event write is delayed - awaiting for ACK from the previous write)
				ExistingEvent("$et-type2", "$>", TFPosToMetadata(_tfPos3), "2@test-stream");

				NoStream("$et");
			}

			protected override IEnumerable<WhenStep> When() {
				var fromZeroPosition = CheckpointTag.FromEventTypeIndexPositions(
					0, new TFPos(0, -1), new Dictionary<string, long> {{"type1", -1}, {"type2", -1}});
				yield return
					new ReaderSubscriptionManagement.Subscribe(
						_subscriptionId, fromZeroPosition, _readerStrategy, _readerSubscriptionOptions);

				// simulate index-by-type system projection (ACK received - writing the next event)
				yield return
					new ClientMessage.WriteEvents(
						Guid.NewGuid(), Guid.NewGuid(), new NoopEnvelope(), false, "$et-type1", ExpectedVersion.Any,
						new Event(Guid.NewGuid(), "$>", false, "1@test-stream", TFPosToMetadata(_tfPos2)), user: null);
			}
		}
	}
}
