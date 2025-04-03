// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

public abstract class race_conditions_when_successive_writes_are_quick {
	public abstract class
		Base<TLogFormat, TStreamId> :
			TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
		protected static readonly Type FakeProjectionType = typeof(FakeProjection);
		protected const string _projectionSource = @"";
		protected const string _projection1 = "projection#1";
		protected const string _projection2 = "projection#2";

		protected override void Given() {
			base.Given();
			NoOtherStreams();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		}

		protected override ManualQueue GiveInputQueue() {
			return new ManualQueue(_bus, new RealTimeProvider());
		}

		protected void Process() {
			int count = 1;
			while (count > 0) {
				count = 0;
				count += _queue.ProcessNonTimer();
			}
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), true, true)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), true, false)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), false, true)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), false, false)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), true, true)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), true, false)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), false, true)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), false, false)]
	public class create_create_race_condition<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		
		private readonly bool shouldBatchCreate1;
		private readonly bool shouldBatchCreate2;
		public create_create_race_condition(bool shouldBatchCreate1, bool shouldBatchCreate2) {
			this.shouldBatchCreate1 = shouldBatchCreate1;
			this.shouldBatchCreate2 = shouldBatchCreate2;
		}
		
		private WhenStep GetCreate(string name, bool batch) {
			if (batch) {
				var projectionPost = new ProjectionManagementMessage.Command.PostBatch.ProjectionPost(
					ProjectionMode.Continuous, ProjectionManagementMessage.RunAs.System, name,
					"native:" + FakeProjectionType.AssemblyQualifiedName, enabled: true,
					checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: false, query: _projectionSource, enableRunAs: true);
				return (new ProjectionManagementMessage.Command.PostBatch(_bus,
					ProjectionManagementMessage.RunAs.System, new[] { projectionPost }));
			}

			return (new ProjectionManagementMessage.Command.Post(_bus, ProjectionMode.Continuous,
				name,
				ProjectionManagementMessage.RunAs.System, "native:" + FakeProjectionType.AssemblyQualifiedName,
				_projectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false));
		}
		
		protected override void Given() {
			base.Given();
			AllWritesQueueUp();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return GetCreate(_projection1, shouldBatchCreate1);
			yield return GetCreate(_projection2, shouldBatchCreate2);
		}

		[Test]
		public void no_WrongExpectedVersion_for_create_create() {
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<ProjectionManagementMessage.Command.Post>().Count() + _queue.TimerMessagesOfType<ProjectionManagementMessage.Command.PostBatch>().Count() > 0) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection1, "dummy"));
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is ProjectionManagementMessage.ProjectionState);
			Assert.IsTrue(fakeEnvelope.Replies[1] is ProjectionManagementMessage.ProjectionState);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), true)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), false)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), true)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), false)]
	public class create_delete_race_condition<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		private readonly bool shouldBatchCreate;
		public create_delete_race_condition(bool shouldBatchCreate) {
			this.shouldBatchCreate = shouldBatchCreate;
		}
		
		protected override void Given() {
			base.Given();
			AllWritesSucceed();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return
				(new ProjectionManagementMessage.Command.Post(_bus, ProjectionMode.Continuous,
					_projection1,
					ProjectionManagementMessage.RunAs.System, "native:" + FakeProjectionType.AssemblyQualifiedName,
					_projectionSource, enabled: true, checkpointsEnabled: true,
					emitEnabled: false, trackEmittedStreams: false));
		}

		private IEnumerable<WhenStep> TestMessages(Guid projectionToDeletedId) {
			yield return GetCreate(_projection2);
			yield return
				(new ProjectionManagementMessage.Internal.Deleted(_projection1, projectionToDeletedId));
		}
		
		private WhenStep GetCreate(string name) {
			if (shouldBatchCreate) {
				var projectionPost = new ProjectionManagementMessage.Command.PostBatch.ProjectionPost(
					ProjectionMode.Continuous, ProjectionManagementMessage.RunAs.System, name,
					"native:" + FakeProjectionType.AssemblyQualifiedName, enabled: true,
					checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: false, query: _projectionSource, enableRunAs: true);
				return (new ProjectionManagementMessage.Command.PostBatch(_bus,
					ProjectionManagementMessage.RunAs.System, new[] { projectionPost }));
			}

			return (new ProjectionManagementMessage.Command.Post(_bus, ProjectionMode.Continuous,
				name,
				ProjectionManagementMessage.RunAs.System, "native:" + FakeProjectionType.AssemblyQualifiedName,
				_projectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false));
		}

		private Guid GetProjectionId(string name) {
			var field = _manager.GetType().GetField("_projectionsMap",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			var projectionsMap = (Dictionary<Guid, string>)field!.GetValue(_manager);
			foreach (var entry in projectionsMap!) {
				if (entry.Value.Equals(name)) {
					return entry.Key;
				}
			}

			return Guid.Empty;
		}

		[Test]
		public void no_WrongExpectedVersion_for_create_delete() {
			Guid projection1Id = GetProjectionId(_projection1);
			AllWritesSucceed(false);
			AllWritesQueueUp();
			WhenLoop(TestMessages(projection1Id));
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<ProjectionManagementMessage.Internal.Deleted>().Count() > 0) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection1, "dummy"));
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is ProjectionManagementMessage.NotFound);
			Assert.IsTrue(fakeEnvelope.Replies[1] is ProjectionManagementMessage.ProjectionState);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), true)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), false)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), true)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), false)]
	public class delete_create_race_condition<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		private readonly bool shouldBatchCreate;

		public delete_create_race_condition(bool shouldBatchCreate) {
			this.shouldBatchCreate = shouldBatchCreate;
		}

		protected override void Given() {
			base.Given();
			AllWritesSucceed();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return (new ProjectionManagementMessage.Command.Post(_bus,
				ProjectionMode.Continuous,
				_projection1,
				ProjectionManagementMessage.RunAs.System, "native:" + FakeProjectionType.AssemblyQualifiedName,
				_projectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false));
		}

		private WhenStep GetCreate(string name) {
			if (shouldBatchCreate) {
				var projectionPost = new ProjectionManagementMessage.Command.PostBatch.ProjectionPost(
					ProjectionMode.Continuous, ProjectionManagementMessage.RunAs.System, name,
					"native:" + FakeProjectionType.AssemblyQualifiedName, enabled: true,
					checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: false, query: _projectionSource, enableRunAs: true);
				return (new ProjectionManagementMessage.Command.PostBatch(_bus,
					ProjectionManagementMessage.RunAs.System, new[] { projectionPost }));
			}

			return (new ProjectionManagementMessage.Command.Post(_bus, ProjectionMode.Continuous,
				name,
				ProjectionManagementMessage.RunAs.System, "native:" + FakeProjectionType.AssemblyQualifiedName,
				_projectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false));
		}

		private IEnumerable<WhenStep> TestMessages(Guid projectionToDeletedId) {
			yield return
				(new ProjectionManagementMessage.Internal.Deleted(_projection1, projectionToDeletedId));
			yield return GetCreate(_projection2);
		}

		private Guid GetProjectionId(string name) {
			var field = _manager.GetType().GetField("_projectionsMap",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			var projectionsMap = (Dictionary<Guid, string>)field!.GetValue(_manager);
			foreach (var entry in projectionsMap!) {
				if (entry.Value.Equals(name)) {
					return entry.Key;
				}
			}

			return Guid.Empty;
		}

		[Test]
		public void no_WrongExpectedVersion_for_delete_create() {
			Guid projection1Id = GetProjectionId(_projection1);
			AllWritesSucceed(false);
			AllWritesQueueUp();
			WhenLoop(TestMessages(projection1Id));
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<ProjectionManagementMessage.Command.Post>().Count() + _queue.TimerMessagesOfType<ProjectionManagementMessage.Command.PostBatch>().Count() > 0) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection1, "dummy"));
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is ProjectionManagementMessage.NotFound);
			Assert.IsTrue(fakeEnvelope.Replies[1] is ProjectionManagementMessage.ProjectionState);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class delete_delete_race_condition<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		protected override void Given() {
			base.Given();
			AllWritesSucceed();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When()) yield return m;
			yield return
				(new ProjectionManagementMessage.Command.Post(_bus, ProjectionMode.Continuous,
					_projection1,
					ProjectionManagementMessage.RunAs.System, "native:" + FakeProjectionType.AssemblyQualifiedName,
					_projectionSource, enabled: true, checkpointsEnabled: true,
					emitEnabled: false, trackEmittedStreams: false));
			yield return
				(new ProjectionManagementMessage.Command.Post(_bus, ProjectionMode.Continuous,
					_projection2,
					ProjectionManagementMessage.RunAs.System, "native:" + FakeProjectionType.AssemblyQualifiedName,
					_projectionSource, enabled: true, checkpointsEnabled: true,
					emitEnabled: false, trackEmittedStreams: false));
		}

		private IEnumerable<WhenStep> TestMessages(Guid projectionToDeletedId1, Guid projectionToDeletedId2) {
			yield return
				(new ProjectionManagementMessage.Internal.Deleted(_projection1, projectionToDeletedId1));
			yield return
				(new ProjectionManagementMessage.Internal.Deleted(_projection2, projectionToDeletedId2));
		}

		private Guid GetProjectionId(string name) {
			var field = _manager.GetType().GetField("_projectionsMap",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			var projectionsMap = (Dictionary<Guid, string>)field!.GetValue(_manager);
			foreach (var entry in projectionsMap!) {
				if (entry.Value.Equals(name)) {
					return entry.Key;
				}
			}

			return Guid.Empty;
		}

		[Test]
		public void no_WrongExpectedVersion_for_delete_create() {
			Guid projection1Id = GetProjectionId(_projection1);
			Guid projection2Id = GetProjectionId(_projection2);
			AllWritesSucceed(false);
			AllWritesQueueUp();
			WhenLoop(TestMessages(projection1Id, projection2Id));
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<ProjectionManagementMessage.Internal.Deleted>().Count() > 0) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0,
				_consumer.HandledMessages.OfType<ProjectionManagementMessage.OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection1, "dummy"));
			_manager.Handle(new ProjectionManagementMessage.Command.GetState(fakeEnvelope, _projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is ProjectionManagementMessage.NotFound);
			Assert.IsTrue(fakeEnvelope.Replies[1] is ProjectionManagementMessage.NotFound);
		}
	}
}
