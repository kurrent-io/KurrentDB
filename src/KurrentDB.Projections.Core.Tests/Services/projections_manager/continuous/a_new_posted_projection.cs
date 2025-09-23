// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager.continuous;

public static class a_new_posted_projection {
	public abstract class Base<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
		protected string _projectionName;
		protected string _projectionSource;
		protected Type _fakeProjectionType;
		protected ProjectionMode _projectionMode;
		protected bool _checkpointsEnabled;
		protected bool _trackEmittedStreams;
		protected bool _emitEnabled;
		protected bool _projectionEnabled;

		protected override void Given() {
			base.Given();

			_projectionName = "test-projection";
			_projectionSource = "";
			_fakeProjectionType = typeof(FakeProjection);
			_projectionMode = ProjectionMode.Continuous;
			_checkpointsEnabled = true;
			_trackEmittedStreams = true;
			_emitEnabled = true;
			_projectionEnabled = true;

			NoStream("$projections-test-projection-checkpoint");
			NoStream("$projections-test-projection-order");
			NoOtherStreams();
			AllWritesSucceed();
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
			yield return new ProjectionManagementMessage.Command.Post(
				_bus, _projectionMode, _projectionName,
				ProjectionManagementMessage.RunAs.System,
				$"native:{_fakeProjectionType.AssemblyQualifiedName}", _projectionSource,
				enabled: _projectionEnabled,
				checkpointsEnabled: _checkpointsEnabled, trackEmittedStreams: _trackEmittedStreams,
				emitEnabled: _emitEnabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_get_query<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return new ProjectionManagementMessage.Command.GetQuery(_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous);
		}

		[Test]
		public void returns_correct_source() {
			Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Count());
			var projectionQuery = _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Single();
			Assert.AreEqual(_projectionName, projectionQuery.Name);
			Assert.AreEqual("", projectionQuery.Query);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_get_state<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override void Given() {
			base.Given();
			EnableReadAll();
			ExistingEvent("temp1", "test1", "{}", "{}");
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return new ProjectionManagementMessage.Command.GetState(_bus, _projectionName, "");
		}

		[Test]
		public void returns_correct_state() {
			var actual = _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().ToArray();
			Assert.AreEqual(1, actual.Length);
			Assert.AreEqual(_projectionName, actual.Single().Name);
			//at least projection initializaed message is here
			Assert.AreEqual("{\"data\": 1}", actual.Single().State);
			Assert.AreEqual(_all.Last(v => !SystemStreams.IsSystemStream(v.Value.EventStreamId)).Key, actual.Single().Position.Position);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_failing<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			var readerAssignedMessage = _consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>().LastOrDefault();
			Assert.IsNotNull(readerAssignedMessage);
			var reader = readerAssignedMessage.ReaderId;

			yield return
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					reader, new TFPos(100, 50), new TFPos(100, 50), "stream", 1, "stream", 1, false, Guid.NewGuid(),
					"fail", false, [], [], 100, 33.3f);
		}

		[Test]
		public void publishes_faulted_message() {
			Assert.AreEqual(1, _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Faulted>().Count());
		}

		[Test]
		public void the_projection_status_becomes_faulted() {
			_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName));

			var actual = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().ToArray();
			Assert.AreEqual(1, actual.Length);
			Assert.AreEqual(1, actual.Single().Projections.Length);
			Assert.AreEqual(_projectionName, actual.Single().Projections.Single().Name);
			Assert.AreEqual(ManagedProjectionState.Faulted, actual.Single().Projections.Single().LeaderStatus);
		}
	}
}

public class an_expired_projection {
	public abstract class Base<TLogFormat, TStreamId> : a_new_posted_projection.Base<TLogFormat, TStreamId> {
		protected Guid Reader;

		protected override void Given() {
			AllWritesSucceed();
			base.Given();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			var readerAssignedMessage =
				_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>()
					.LastOrDefault();
			Assert.IsNotNull(readerAssignedMessage);
			Reader = readerAssignedMessage.ReaderId;

			yield return
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Reader, new TFPos(100, 50), new TFPos(100, 50), "stream", 1, "stream", 1, false,
					Guid.NewGuid(),
					"type", false, [], [], 100, 33.3f);
			_timeProvider.AddToUtcTime(TimeSpan.FromMinutes(6));
			yield return Yield;
			foreach (var m in _consumer.HandledMessages.OfType<TimerMessage.Schedule>().ToArray())
				m.Envelope.ReplyWith(m.ReplyMessage);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_statistics<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var s in base.When())
				yield return s;
			_consumer.HandledMessages.Clear();
			yield return new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName);
		}

		[Test]
		public void projection_is_not_deleted() {
			Assert.IsFalse(_consumer.HandledMessages.OfType<ProjectionManagementMessage.NotFound>().Any());
			var res = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
				.First(x => x.Projections.Any(y => y.Name == _projectionName));
			Assert.AreEqual("Running", res.Projections.First(x => x.Name == _projectionName).Status);
		}
	}
}
