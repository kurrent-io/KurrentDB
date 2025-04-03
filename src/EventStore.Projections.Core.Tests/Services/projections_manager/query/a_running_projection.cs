// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.query {
	namespace a_running_projection {
		public abstract class Base<TLogFormat, TStreamId> : a_new_posted_projection.Base<TLogFormat, TStreamId> {
			protected Guid _reader;

			protected override void Given() {
				base.Given();
				AllWritesSucceed();
				NoOtherStreams();
			}

			protected override IEnumerable<WhenStep> When() {
				foreach (var m in base.When()) yield return m;
				var readerAssignedMessage =
					_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>()
						.LastOrDefault();
				Assert.IsNotNull(readerAssignedMessage);
				_reader = readerAssignedMessage.ReaderId;
				_consumer.HandledMessages.Clear();
				yield return
					(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
						_reader, new TFPos(100, 50), new TFPos(100, 50), "stream", 1, "stream", 1, false,
						Guid.NewGuid(),
						"type", false, new byte[0], new byte[0], 100, 33.3f));
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_handling_eof<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
			protected override IEnumerable<WhenStep> When() {
				foreach (var m in base.When()) yield return m;

				yield return (new ReaderSubscriptionMessage.EventReaderEof(_reader));
			}

			[Test]
			public void pause_message_is_published() {
				Assert.Inconclusive("actually in unsubscribes...");
			}


			[Test]
			public void the_projection_status_becomes_completed_enabled() {
				_manager.Handle(
					new ProjectionManagementMessage.Command.GetStatistics(
						_bus, null, _projectionName, false));

				Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Count());
				Assert.AreEqual(
					1,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Length);
				Assert.AreEqual(
					_projectionName,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Single()
						.Name);
				Assert.AreEqual(
					ManagedProjectionState.Completed,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Single()
						.LeaderStatus);
				Assert.AreEqual(
					true,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Single()
						.Enabled);
			}

			[Test]
			public void writes_result_stream() {
				List<EventRecord> resultsStream;
				Assert.IsTrue((_streams.TryGetValue("$projections-test-projection-result", out resultsStream)));
				Assert.AreEqual(1 + 1 /* $Eof*/, resultsStream.Count);
				Assert.AreEqual("{\"data\": 1}", Encoding.UTF8.GetString(resultsStream[0].Data.Span));
			}

			[Test]
			public void does_not_write_to_any_other_streams() {
				Assert.IsEmpty(
					HandledMessages.OfType<ClientMessage.WriteEvents>()
						.Where(v => v.EventStreamId != "$projections-test-projection-result")
						.Where(v => v.EventStreamId != "$$$projections-test-projection-result")
						.Select(v => v.EventStreamId));
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_handling_event<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
			protected override IEnumerable<WhenStep> When() {
				foreach (var m in base.When()) yield return m;
				yield return
					(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
						_reader, new TFPos(200, 150), new TFPos(200, 150), "stream", 2, "stream", 1, false,
						Guid.NewGuid(), "type", false, new byte[0], new byte[0], 100, 33.3f));
			}

			[Test]
			public void the_projection_status_remains_running_enabled() {
				_manager.Handle(
					new ProjectionManagementMessage.Command.GetStatistics(
						_bus, null, _projectionName, false));

				Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Count());
				Assert.AreEqual(
					1,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Length);
				Assert.AreEqual(
					_projectionName,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Single()
						.Name);
				Assert.AreEqual(
					ManagedProjectionState.Running,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Single()
						.LeaderStatus);
				Assert.AreEqual(
					true,
					_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
						.Single()
						.Projections.Single()
						.Enabled);
			}
		}
	}
}
