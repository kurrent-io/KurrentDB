// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messaging;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messages.EventReaders.Feeds;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.feed_reader {
	namespace feed_reader_by_event_indexes {
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_reading_the_first_event<TLogFormat, TStreamId> : TestFixtureWithFeedReaderService<TLogFormat, TStreamId> {
			private QuerySourcesDefinition _querySourcesDefinition;
			private CheckpointTag _fromPosition;
			private int _maxEvents;

			private TFPos _tfPos1;
			//private TFPos _tfPos2;
			//private TFPos _tfPos3;

			protected override void Given() {
				base.Given();
				_tfPos1 = ExistingEvent("test-stream", "type1", "{}", "{Data: 1}");
				//_tfPos2 = ExistingEvent("test-stream", "type1", "{}", "{Data: 2}");
				//_tfPos3 = ExistingEvent("test-stream", "type2", "{}", "{Data: 3}");

				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos1), "0@test-stream");
				NoStream("$et-type2");
				NoStream("$et");
				//NOTE: the following events should be late written
				//ExistingEvent("$et-type2", "$>", "", "2@test-stream");
				//ExistingEvent("$et-type1", "$>", "", "1@test-stream");

				_querySourcesDefinition = new QuerySourcesDefinition {
					AllStreams = true,
					Events = new[] {"type1", "type2"},
					Options = new QuerySourcesDefinitionOptions { }
				};
				_fromPosition = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(0, -1),
					new Dictionary<string, long> {{"type1", -1}, {"type2", -1}});
				_maxEvents = 1; // reading the first event
			}

			private string TFPosToMetadata(TFPos tfPos) {
				return string.Format(@"{{""$c"":{0},""$p"":{1}}}", tfPos.CommitPosition, tfPos.PreparePosition);
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
				Assert.AreEqual(
					CheckpointTag.FromEventTypeIndexPositions(0, _tfPos1,
						new Dictionary<string, long> {{"type1", 0}, {"type2", -1}}), feedPage.LastReaderPosition);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_reading_the_reordered_events_from_the_same_stream<TLogFormat, TStreamId> : TestFixtureWithFeedReaderService<TLogFormat, TStreamId> {
			private QuerySourcesDefinition _querySourcesDefinition;
			private CheckpointTag _fromPosition;
			private int _maxEvents;
			private TFPos _tfPos1;
			private TFPos _tfPos2;
			private TFPos _tfPos3;

			protected override void Given() {
				base.Given();
				_tfPos1 = ExistingEvent("test-stream", "type1", "{}", "{Data: 1}");
				_tfPos2 = ExistingEvent("test-stream", "type1", "{}", "{Data: 2}");
				_tfPos3 = ExistingEvent("test-stream", "type2", "{}", "{Data: 3}");

				// writes reordered due to batching or timeouts in system projection
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos1), "0@test-stream");
				ExistingEvent("$et-type2", "$>", TFPosToMetadata(_tfPos3), "2@test-stream");
				ExistingEvent("$et-type1", "$>", TFPosToMetadata(_tfPos2), "1@test-stream");
				NoStream("$et");
				_querySourcesDefinition = new QuerySourcesDefinition {
					AllStreams = true,
					Events = new[] {"type1", "type2"},
					Options = new QuerySourcesDefinitionOptions { }
				};
				_fromPosition = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(0, -1),
					new Dictionary<string, long> {{"type1", -1}, {"type2", -1}});
				_maxEvents = 3;
			}

			private string TFPosToMetadata(TFPos tfPos) {
				return string.Format(@"{{""$c"":{0},""$p"":{1}}}", tfPos.CommitPosition, tfPos.PreparePosition);
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
				Assert.AreEqual(
					CheckpointTag.FromEventTypeIndexPositions(0, _tfPos3,
						new Dictionary<string, long> {{"type1", 1}, {"type2", 0}}), feedPage.LastReaderPosition);
			}

			[Test]
			public void returns_correct_event_sequence() {
				var feedPage = _consumer.HandledMessages.OfType<FeedReaderMessage.FeedPage>().Single();
				Assert.That(
					new long[] {0, 1, 2}.SequenceEqual(
						feedPage.Events.Select(e => e.ResolvedEvent.EventSequenceNumber).OrderBy(v => v)));
			}
		}
	}
}
