// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.bi_state;

public static class a_new_posted_projection {
	public abstract class Base<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
		protected string _projectionName;
		protected string _projectionSource;
		protected Type _fakeProjectionType;
		protected ProjectionMode _projectionMode;
		protected bool _checkpointsEnabled;
		protected bool _trackEmittedStreams;
		protected bool _emitEnabled;

		protected override void Given() {
			base.Given();
			AllWritesSucceed();
			NoOtherStreams();

			_projectionName = "test-projection";
			_projectionSource = @"";
			_fakeProjectionType = typeof(FakeBiStateProjection);
			_projectionMode = ProjectionMode.Continuous;
			_checkpointsEnabled = true;
			_trackEmittedStreams = true;
			_emitEnabled = false;
		}

		protected override IEnumerable<WhenStep> When() {
			yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
			yield return
				(new ProjectionManagementMessage.Command.Post(
					_bus, _projectionMode, _projectionName,
					ProjectionManagementMessage.RunAs.System, "native:" + _fakeProjectionType.AssemblyQualifiedName,
					_projectionSource, enabled: true, checkpointsEnabled: _checkpointsEnabled,
					trackEmittedStreams: _trackEmittedStreams,
					emitEnabled: _emitEnabled));
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_get_state<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return (
				new ProjectionManagementMessage.Command.GetState(_bus, _projectionName, ""));
		}

		[Test]
		public void returns_correct_state() {
			Assert.AreEqual(
				1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Count());
			Assert.AreEqual(
				_projectionName,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Single().Name);
			Assert.AreEqual(
				"", _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Single().State);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_stopping<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		private Guid _reader;

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;

			var readerAssignedMessage =
				_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>()
					.LastOrDefault();
			Assert.IsNotNull(readerAssignedMessage);
			_reader = readerAssignedMessage.ReaderId;

			yield return
				(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					_reader, new TFPos(100, 50), new TFPos(100, 50), "stream1", 1, "stream1", 1, false,
					Guid.NewGuid(),
					"type", false, Helper.UTF8NoBom.GetBytes("1"), new byte[0], 100, 33.3f));

			yield return
				(ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					_reader, new TFPos(200, 150), new TFPos(200, 150), "stream2", 1, "stream2", 1, false,
					Guid.NewGuid(),
					"type", false, Helper.UTF8NoBom.GetBytes("1"), new byte[0], 100, 33.3f));

			yield return
				new ProjectionManagementMessage.Command.Disable(
					_bus, _projectionName, ProjectionManagementMessage.RunAs.System);
		}

		[Test]
		public void writes_both_stream_and_shared_partition_checkpoints() {
			var writeProjectionCheckpoints =
				HandledMessages.OfType<ClientMessage.WriteEvents>()
					.OfEventType(ProjectionEventTypes.ProjectionCheckpoint).ToArray();
			var writeCheckpoints =
				HandledMessages.OfType<ClientMessage.WriteEvents>()
					.OfEventType(ProjectionEventTypes.PartitionCheckpoint).ToArray();

			Assert.AreEqual(1, writeProjectionCheckpoints.Length);
			Assert.AreEqual(@"[{""data"": 2}]", Encoding.UTF8.GetString(writeProjectionCheckpoints[0].Data));
			Assert.AreEqual(2, writeCheckpoints.Length);

			Assert.That(
				writeCheckpoints.All(
					v => Encoding.UTF8.GetString(v.Data) == @"[{""data"": 1},{""data"": 1}]"));
		}
	}
}
