// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Tests.Services.event_reader.event_by_type_index_reader.catching_up {
	namespace index_checkpoint {
		abstract class with_some_indexed_events<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
			protected Guid _subscriptionId;
			private QuerySourcesDefinition _sourceDefinition;
			protected IReaderStrategy _readerStrategy;
			protected ReaderSubscriptionOptions _readerSubscriptionOptions;
			protected TFPos _tfPos1;
			protected TFPos _tfPos2;
			protected TFPos _tfPos3;

			protected override bool GivenHeadingReaderRunning() {
				// make sure it does not produce read-all-forward messages
				return false;
			}

			protected override void Given() {
				base.Given();
				AllWritesSucceed();
				_tfPos1 = ExistingEvent("test-stream", "type1", "{}", "{Data: 1}");
				_tfPos2 = ExistingEvent("test-stream", "type2", "{}", "{Data: 2}");

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
			public void returns_events_in_original_order() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				Assert.That(
					(from e in receivedEvents
						orderby e.Data.EventSequenceNumber
						select e.Data.EventSequenceNumber)
					.SequenceEqual(from e in receivedEvents
						select e.Data.EventSequenceNumber),
					"Incorrect event order received");
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_index_checkpoint_is_written_while_idle<TLogFormat, TStreamId> : with_some_indexed_events<TLogFormat, TStreamId> {
			protected override void GivenInitialIndexState() {
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos1), "0@test-stream");


				// NOTE: do not configure $et and $et-type2 to delay ReadCompleted on this stream 
				// ExistingEvent("$et-type2", "$>", TFPosToMetadata(_tfPos2), "1@test-stream");
				// NoStream("$et");
			}

			protected override IEnumerable<WhenStep> When() {
				var fromZeroPosition = CheckpointTag.FromEventTypeIndexPositions(
					0, new TFPos(0, -1), new Dictionary<string, long> {{"type1", -1}, {"type2", -1}});
				yield return
					new ReaderSubscriptionManagement.Subscribe(
						_subscriptionId, fromZeroPosition, _readerStrategy, _readerSubscriptionOptions);
				//DisableTimer();
				yield return CreateWriteEvent("test-stream", "type1", """{"Data": 3}""", "{}");
				_tfPos3 = _all.Last(v => v.Value.EventStreamId == "test-stream").Key;

				yield return
					CreateWriteEvent("$et-type1", "$>", "2@test-stream", TFPosToMetadata(_tfPos3), isJson: false);

				yield return CreateWriteEvent("$et-type2", "$>", "1@test-stream", TFPosToMetadata(_tfPos2));

				yield return CreateWriteEvent("$et", ProjectionEventTypes.PartitionCheckpoint, TFPosToMetadata(_tfPos2),
					TFPosToMetadata(_tfPos2));

				// we are still in index-based reading mode
				Assert.IsEmpty(_consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>());

				// simulate late response to the read request in this particular order
				yield return
					_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>()
						.Single(v => v.EventStreamId == "$et");

				/*yield return
				    _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
				        .Single(v => v.EventStreamId == "$et-type2");
				EnableTimer();*/
			}

			[Test]
			public void returns_all_events() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				Assert.AreEqual(3, receivedEvents.Length);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_the_index_checkpoint_is_read_last<TLogFormat, TStreamId> : with_some_indexed_events<TLogFormat, TStreamId> {
			protected override void GivenInitialIndexState() {
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos1), "0@test-stream");
				ExistingEvent("$et-type2", "$>", TFPosToMetadata(_tfPos2), "1@test-stream");


				// NOTE: do not configure $et to delay ReadCompleted on this stream 
				// NoStream("$et");
			}

			protected override IEnumerable<WhenStep> When() {
				var fromZeroPosition = CheckpointTag.FromEventTypeIndexPositions(
					0, new TFPos(0, -1), new Dictionary<string, long> {{"type1", -1}, {"type2", -1}});
				yield return
					new ReaderSubscriptionManagement.Subscribe(
						_subscriptionId, fromZeroPosition, _readerStrategy, _readerSubscriptionOptions);
			}

			[Test]
			public void stays_in_index_based_reading_mode() {
				Assert.IsEmpty(_consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>());
			}

			[Test]
			public void returns_just_first_event_which_is_safe_to_return() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				//NOTE: 
				// the first event is safe to read as we know the next event in another stream
				// the second event is not safe as there is no more events in the first stream and checkpoint is not yet available
				Assert.AreEqual(1, receivedEvents.Length);
			}
		}
	}
}
