// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager.query;

public class a_failed_projection {
	public abstract class Base<TLogFormat, TStreamId> : a_new_posted_projection.Base<TLogFormat, TStreamId> {
		protected override void Given() {
			base.Given();
			_projectionSource = "fail";
			NoOtherStreams();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			var readerAssignedMessage = _consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>().LastOrDefault();
			Assert.IsNotNull(readerAssignedMessage);
			var reader = readerAssignedMessage.ReaderId;
			yield return
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					reader, new TFPos(100, 50), new TFPos(100, 50), "stream", 1, "stream", 1, false, Guid.NewGuid(),
					"event", false, [], [], 100, 33.3f);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_updating_query<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return
				new ProjectionManagementMessage.Command.UpdateQuery(_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous, "", null);
		}

		[Test]
		public void the_projection_status_becomes_running() {
			_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName));

			var actual = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().ToArray();
			Assert.AreEqual(1, actual.Length);
			Assert.AreEqual(1, actual.Single().Projections.Length);
			Assert.AreEqual(_projectionName, actual.Single().Projections.Single().Name);
			Assert.AreEqual(ManagedProjectionState.Running, actual.Single().Projections.Single().LeaderStatus);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_stopping<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return new ProjectionManagementMessage.Command.Disable(_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous);
		}

		[Test]
		public void the_projection_status_becomes_faulted_disabled() {
			_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName));

			var actual = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().ToArray();
			Assert.AreEqual(1, actual.Length);
			Assert.AreEqual(1, actual.Single().Projections.Length);
			Assert.AreEqual(_projectionName, actual.Single().Projections.Single().Name);
			Assert.AreEqual(ManagedProjectionState.Stopped, actual.Single().Projections.Single().LeaderStatus);
			Assert.AreEqual(false, actual.Single().Projections.Single().Enabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_starting<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return new ProjectionManagementMessage.Command.Enable(_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous);
		}

		[Test]
		public void the_projection_status_becomes_running_enabled() // as we restart
		{
			_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName));

			var actual = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().ToArray();
			Assert.AreEqual(1, actual.Length);
			Assert.AreEqual(1, actual.Single().Projections.Length);
			Assert.AreEqual(_projectionName, actual.Single().Projections.Single().Name);
			Assert.AreEqual(ManagedProjectionState.Running, actual.Single().Projections.Single().LeaderStatus);
			Assert.AreEqual(true, actual.Single().Projections.Single().Enabled);
		}
	}
}
