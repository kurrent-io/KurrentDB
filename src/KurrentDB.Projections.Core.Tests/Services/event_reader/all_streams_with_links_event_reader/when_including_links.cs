// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.all_streams_with_links_event_reader {
	namespace when_including_links {
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_reading<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
			protected Guid _subscriptionId;
			private QuerySourcesDefinition _sourceDefinition;
			protected IReaderStrategy _readerStrategy;
			protected ReaderSubscriptionOptions _readerSubscriptionOptions;

			protected override bool GivenHeadingReaderRunning() {
				return false;
			}

			protected override void Given() {
				base.Given();
				AllWritesSucceed();
				ExistingEvent("test-stream", "$>", "{}", "{Data: 1}");
				ExistingEvent("test-stream", "$>", "{}", "{Data: 2}");
				ExistingEvent("test-stream", "$>", "{}", "{Data: 3}");

				ExistingEvent("test-stream", "eventType", "{}", "{Data: 4}");
				ExistingEvent("test-stream", "eventType", "{}", "{Data: 5}");
				ExistingEvent("test-stream", "eventType", "{}", "{Data: 6}");
				ExistingEvent("test-stream", "eventType", "{}", "{Data: 7}");

				_subscriptionId = Guid.NewGuid();
				_sourceDefinition = new QuerySourcesDefinition {
					ByStreams = true,
					AllStreams = true,
					AllEvents = true,
					Options = new QuerySourcesDefinitionOptions {
						IncludeLinks = true
					}
				};
				_readerStrategy = ReaderStrategy.Create(
					"test",
					0,
					_sourceDefinition,
					_timeProvider,
					runAs: null);
				_readerSubscriptionOptions = new ReaderSubscriptionOptions(
					checkpointUnhandledBytesThreshold: 10000, checkpointProcessedEventsThreshold: 100,
					checkpointAfterMs: 10000, stopOnEof: true,
					stopAfterNEvents: null,
					enableContentTypeValidation: true);
			}

			protected override IEnumerable<WhenStep> When() {
				var fromZeroPosition = CheckpointTag.FromPosition(0, 0, 0);
				yield return
					new ReaderSubscriptionManagement.Subscribe(
						_subscriptionId, fromZeroPosition, _readerStrategy, _readerSubscriptionOptions);
			}

			[Test]
			public void returns_linked_events() {
				var receivedEvents =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();

				Assert.AreEqual(7, receivedEvents.Length);
			}
		}
	}
}
