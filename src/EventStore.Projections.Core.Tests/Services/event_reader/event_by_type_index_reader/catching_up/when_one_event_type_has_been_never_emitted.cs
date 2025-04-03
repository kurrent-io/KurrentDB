// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Tests.Services.event_reader.event_by_type_index_reader.catching_up {
	namespace when_one_event_type_has_been_never_emitted {
		abstract class with_one_event_type_has_been_never_emitted<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
			protected const int TailLength = 10;
			protected Guid _subscriptionId;
			private QuerySourcesDefinition _sourceDefinition;
			protected IReaderStrategy _readerStrategy;
			protected ReaderSubscriptionOptions _readerSubscriptionOptions;
			protected TFPos _tfPos1;
			protected TFPos _tfPos2;
			protected TFPos _tfPos3;
			protected TFPos[] _tfPos;

			protected override bool GivenHeadingReaderRunning() {
				return true;
			}

			protected override void Given() {
				base.Given();
				AllWritesSucceed();
				_tfPos = new TFPos[TailLength];
				_tfPos1 = ExistingEvent("test-stream", "type1", "{}", "{Data: 1}");
				_tfPos2 = ExistingEvent("test-stream", "type1", "{}", "{Data: 2}");
				_tfPos3 = ExistingEvent("test-stream", "type1", "{}", "{Data: 3}");

				for (var i = 0; i < TailLength; i++) {
					_tfPos[i] = ExistingEvent("test-stream", "type1", "{}", "{Data: " + i + "}");
				}

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

				Assert.AreEqual(TailLength + 3, receivedEvents.Length);
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
		class when_index_checkpoint_multiple_events_behind<TLogFormat, TStreamId> : with_one_event_type_has_been_never_emitted<TLogFormat, TStreamId> {
			protected override void GivenInitialIndexState() {
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos1), "0@test-stream");
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos2), "1@test-stream");
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos3), "2@test-stream");

				for (var i = 0; i < TailLength; i++)
					ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos[i]), (i + 3) + "@test-stream");

				NoStream("$et-type2");
				ExistingEvent("$et", ProjectionEventTypes.PartitionCheckpoint, TFPosToMetadata(_tfPos3),
					TFPosToMetadata(_tfPos3));
			}

			protected override IEnumerable<WhenStep> When() {
				var fromZeroPosition = CheckpointTag.FromEventTypeIndexPositions(
					0, new TFPos(0, -1), new Dictionary<string, long> {{"type1", -1}, {"type2", -1}});
				yield return
					new ReaderSubscriptionManagement.Subscribe(
						_subscriptionId, fromZeroPosition, _readerStrategy, _readerSubscriptionOptions);
			}
		}
	}
}
