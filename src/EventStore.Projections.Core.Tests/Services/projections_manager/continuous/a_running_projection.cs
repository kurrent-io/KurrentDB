// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.continuous;

public class a_running_projection {
	public abstract class Base<TLogFormat, TStreamId> : a_new_posted_projection.Base<TLogFormat, TStreamId> {
		protected Guid _reader;

		protected override void Given() {
			base.Given();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			var readerAssignedMessage =
				_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>()
					.LastOrDefault();
			if (_projectionEnabled) {
				Assert.IsNotNull(readerAssignedMessage);
				_reader = readerAssignedMessage.ReaderId;

				yield return
					(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
						_reader, new TFPos(100, 50), new TFPos(100, 50), "stream", 1, "stream", 1, false,
						Guid.NewGuid(), "type", false, new byte[0], new byte[0], 100, 33.3f));
			} else
				_reader = Guid.Empty;
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_stopping<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;

			yield return
				(new ProjectionManagementMessage.Command.Disable(
					_bus, _projectionName, ProjectionManagementMessage.RunAs.System));
			for (var i = 0; i < 50; i++) {
				yield return
					(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
						_reader, new TFPos(100 * i + 200, 150), new TFPos(100 * i + 200, 150), "stream", 1 + i + 1,
						"stream", 1 + i + 1, false, Guid.NewGuid(), "type", false, new byte[0], new byte[0],
						100 * i + 200, 33.3f));
			}
		}

		[Test]
		public void pause_message_is_published() {
			Assert.Inconclusive("actually in unsubscribes...");
		}

		[Test]
		public void unsubscribe_message_is_published() {
			Assert.Inconclusive("actually in unsubscribes...");
		}


		[Test]
		public void the_projection_status_becomes_stopped_disabled() {
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
				ManagedProjectionState.Stopped,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.LeaderStatus);
			Assert.AreEqual(
				false,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.Enabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_handling_event<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return
				(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					_reader, new TFPos(200, 150), new TFPos(200, 150), "stream", 2, "stream", 2, false,
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

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_resetting<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override void Given() {
			base.Given();
			_projectionEnabled = false;
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;

			yield return
				(new ProjectionManagementMessage.Command.Reset(
					_bus, _projectionName, ProjectionManagementMessage.RunAs.System));
		}

		[Test]
		public void the_projection_epoch_changes() {
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
				1,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.Epoch);
			Assert.AreEqual(
				1,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.Version);
		}

		[Test]
		public void the_projection_status_is_enabled_running() {
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
				ManagedProjectionState.Stopped,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.LeaderStatus);
			Assert.AreEqual(
				false,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.Enabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_resetting_and_starting<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override void Given() {
			base.Given();
			_projectionEnabled = false;
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return
				(new ProjectionManagementMessage.Command.Reset(
					_bus, _projectionName, ProjectionManagementMessage.RunAs.System));
			yield return
				(new ProjectionManagementMessage.Command.Enable(
					_bus, _projectionName, ProjectionManagementMessage.RunAs.System));
			yield return
				(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					_reader, new TFPos(100, 150), new TFPos(100, 150), "stream", 1 + 1, "stream", 1 + 1, false,
					Guid.NewGuid(), "type", false, new byte[0], new byte[0], 200, 33.3f));
		}

		[Test]
		public void the_projection_epoch_changes() {
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
				1,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.Epoch);
			Assert.AreEqual(
				2,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
					.Single()
					.Projections.Single()
					.Version);
		}

		[Test]
		public void the_projection_status_is_enabled_running() {
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
