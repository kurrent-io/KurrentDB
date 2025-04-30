// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Metrics;
using EventStore.Projections.Core.Tests.Services.projections_manager.continuous;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projection_metrics;

public class A_Projection {
	public class Base<TLogFormat, TStreamId> : a_new_posted_projection.Base<TLogFormat, TStreamId> {
		private bool noStatsYet = true;

		protected IEnumerable<Measurement<long>> ObservedStatus() {
			// for some reason, if GetStatistics is called multiple times, then the stats are duplicated;
			if (noStatsYet) {
				_manager.Handle(
					new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName, false));
				var s = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>();
				_projectionMetricTracker.OnNewStats(s.Single().Projections);
				noStatsYet = false;
			}

			return _projectionMetricTracker.ObserveStatus();
		}

		protected int ObservedRunning() {
			// for some reason, if GetStatistics is called multiple times, then the stats are duplicated;
			if (noStatsYet) {
				_manager.Handle(
					new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName, false));
				var s = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>();
				_projectionMetricTracker.OnNewStats(s.Single().Projections);
				noStatsYet = false;
			}

			return (int)_projectionMetricTracker.ObserveRunning().Single().Value;
		}

		protected int ValueOf(IEnumerable<Measurement<long>> measurements, KeyValuePair<string, object> status)
			=> (int)measurements.ToArray().Single(m => m.Tags.ToArray().Any(t => Equals(t, status))).Value;
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class Stopping<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		[Test]
		public void Has_Correct_Status() {
			Assert.AreEqual(0, ValueOf(ObservedStatus(), ProjectionTracker.StatusRunning), "Status Running");
			Assert.AreEqual(0, ValueOf(ObservedStatus(), ProjectionTracker.StatusFaulted), "Status Faulted");
			Assert.AreEqual(1, ValueOf(ObservedStatus(), ProjectionTracker.StatusStopped), "Status Stopped");
		}

		[Test]
		public void Has_Correct_Running() {
			Assert.AreEqual(0, ObservedRunning(), "Observed  Running");
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return
				new ProjectionManagementMessage.Command.Disable(
					_bus,
					_projectionName,
					ProjectionManagementMessage.RunAs.System
				);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class Faulted<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {

		[Test]
		public void Has_Correct_Status() {
			Assert.AreEqual(0, ValueOf(ObservedStatus(), ProjectionTracker.StatusRunning), "Status Running");
			Assert.AreEqual(1, ValueOf(ObservedStatus(), ProjectionTracker.StatusFaulted), "Status Faulted");
			Assert.AreEqual(0, ValueOf(ObservedStatus(), ProjectionTracker.StatusStopped), "Status Stopped");
		}

		[Test]
		public void Has_Correct_Running() {
			Assert.AreEqual(0, ObservedRunning(), "Observed  Running");
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			var readerAssignedMessage = _consumer.HandledMessages
				.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>()
				.LastOrDefault();
			Assert.IsNotNull(readerAssignedMessage);
			var reader = readerAssignedMessage.ReaderId;

			yield return
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					reader, new TFPos(100, 50), new TFPos(100, 50), "stream", 1, "stream", 1, false, Guid.NewGuid(),
					"fail", false, new byte[0], new byte[0], 100, 33.3f);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class Running<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {

		[Test]
		public void Has_Correct_Status() {
			Assert.AreEqual(1, ValueOf(ObservedStatus(), ProjectionTracker.StatusRunning), "Status Running");
			Assert.AreEqual(0, ValueOf(ObservedStatus(), ProjectionTracker.StatusFaulted), "Status Faulted");
			Assert.AreEqual(0, ValueOf(ObservedStatus(), ProjectionTracker.StatusStopped), "Status Stopped");
		}

		[Test]
		public void Has_Correct_Running() {
			Assert.AreEqual(1, ObservedRunning(), "Observed  Running");
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			var readerAssignedMessage =
				_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>()
					.LastOrDefault();
			Assert.IsNotNull(readerAssignedMessage);
			var reader = readerAssignedMessage.ReaderId;

			yield return
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					reader, new TFPos(100, 50), new TFPos(100, 50), "stream", 1, "stream", 1, false,
					Guid.NewGuid(), "type", false, new byte[0], new byte[0], 100, 33.3f);
		}
	}
}
