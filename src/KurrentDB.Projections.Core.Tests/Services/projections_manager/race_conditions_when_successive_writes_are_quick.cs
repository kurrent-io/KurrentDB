// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.Tests.Services.Replication;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

public abstract class race_conditions_when_successive_writes_are_quick {
	public abstract class Base<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
		protected static readonly Type FakeProjectionType = typeof(FakeProjection);
		protected const string ProjectionSource = "";
		protected const string Projection1 = "projection#1";
		protected const string Projection2 = "projection#2";

		protected override void Given() {
			base.Given();
			NoOtherStreams();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		}

		protected override ManualQueue GiveInputQueue() {
			return new(_bus, new RealTimeProvider());
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
		private readonly bool _shouldBatchCreate1;
		private readonly bool _shouldBatchCreate2;
		public create_create_race_condition(bool shouldBatchCreate1, bool shouldBatchCreate2) {
			_shouldBatchCreate1 = shouldBatchCreate1;
			_shouldBatchCreate2 = shouldBatchCreate2;
		}

		private WhenStep GetCreate(string name, bool batch) {
			if (batch) {
				var projectionPost = new Command.PostBatch.ProjectionPost(
					ProjectionMode.Continuous, RunAs.System, name,
					$"native:{FakeProjectionType.AssemblyQualifiedName}", enabled: true,
					checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: false, query: ProjectionSource, enableRunAs: true);
				return new Command.PostBatch(_bus,
					RunAs.System, [projectionPost]);
			}

			return new Command.Post(_bus, ProjectionMode.Continuous,
				name,
				RunAs.System, $"native:{FakeProjectionType.AssemblyQualifiedName}",
				ProjectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false);
		}

		protected override void Given() {
			base.Given();
			AllWritesQueueUp();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return GetCreate(Projection1, _shouldBatchCreate1);
			yield return GetCreate(Projection2, _shouldBatchCreate2);
		}

		[Test]
		public void no_WrongExpectedVersion_for_create_create() {
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<Command.Post>().Count() + _queue.TimerMessagesOfType<Command.PostBatch>().Count() > 0) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0, _consumer.HandledMessages.OfType<OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection1, "dummy"));
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is ProjectionState);
			Assert.IsTrue(fakeEnvelope.Replies[1] is ProjectionState);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), true)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), false)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), true)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), false)]
	public class create_delete_race_condition<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		private readonly bool _shouldBatchCreate;
		public create_delete_race_condition(bool shouldBatchCreate) {
			_shouldBatchCreate = shouldBatchCreate;
		}

		protected override void Given() {
			base.Given();
			AllWritesSucceed();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return
				new Command.Post(_bus, ProjectionMode.Continuous,
					Projection1,
					RunAs.System, $"native:{FakeProjectionType.AssemblyQualifiedName}",
					ProjectionSource, enabled: true, checkpointsEnabled: true,
					emitEnabled: false, trackEmittedStreams: false);
		}

		private IEnumerable<WhenStep> TestMessages(Guid projectionToDeletedId) {
			yield return GetCreate(Projection2);
			yield return new ProjectionManagementMessage.Internal.Deleted(Projection1, projectionToDeletedId);
		}

		private WhenStep GetCreate(string name) {
			if (_shouldBatchCreate) {
				var projectionPost = new Command.PostBatch.ProjectionPost(
					ProjectionMode.Continuous, RunAs.System, name,
					$"native:{FakeProjectionType.AssemblyQualifiedName}", enabled: true,
					checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: false, query: ProjectionSource, enableRunAs: true);
				return new Command.PostBatch(_bus,
					RunAs.System, [projectionPost]);
			}

			return new Command.Post(_bus, ProjectionMode.Continuous,
				name, RunAs.System, $"native:{FakeProjectionType.AssemblyQualifiedName}",
				ProjectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false);
		}

		private Guid GetProjectionId(string name) {
			var field = _manager.GetType().GetField("_projectionsMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
			Guid projection1Id = GetProjectionId(Projection1);
			AllWritesSucceed(false);
			AllWritesQueueUp();
			WhenLoop(TestMessages(projection1Id));
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<ProjectionManagementMessage.Internal.Deleted>().Any()) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0, _consumer.HandledMessages.OfType<OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection1, "dummy"));
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is NotFound);
			Assert.IsTrue(fakeEnvelope.Replies[1] is ProjectionState);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), true)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), false)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), true)]
	[TestFixture(typeof(LogFormat.V3), typeof(uint), false)]
	public class delete_create_race_condition<TLogFormat, TStreamId> : Base<TLogFormat, TStreamId> {
		private readonly bool _shouldBatchCreate;

		public delete_create_race_condition(bool shouldBatchCreate) {
			_shouldBatchCreate = shouldBatchCreate;
		}

		protected override void Given() {
			base.Given();
			AllWritesSucceed();
		}

		protected override IEnumerable<WhenStep> When() {
			foreach (var m in base.When())
				yield return m;
			yield return new Command.Post(_bus,
				ProjectionMode.Continuous,
				Projection1,
				RunAs.System, $"native:{FakeProjectionType.AssemblyQualifiedName}",
				ProjectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false);
		}

		private WhenStep GetCreate(string name) {
			if (_shouldBatchCreate) {
				var projectionPost = new Command.PostBatch.ProjectionPost(
					ProjectionMode.Continuous, RunAs.System, name,
					$"native:{FakeProjectionType.AssemblyQualifiedName}", enabled: true,
					checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: false, query: ProjectionSource, enableRunAs: true);
				return new Command.PostBatch(_bus,
					RunAs.System, [projectionPost]);
			}

			return new Command.Post(_bus, ProjectionMode.Continuous,
				name, RunAs.System, $"native:{FakeProjectionType.AssemblyQualifiedName}",
				ProjectionSource, enabled: true, checkpointsEnabled: true,
				emitEnabled: false, trackEmittedStreams: false);
		}

		private IEnumerable<WhenStep> TestMessages(Guid projectionToDeletedId) {
			yield return new ProjectionManagementMessage.Internal.Deleted(Projection1, projectionToDeletedId);
			yield return GetCreate(Projection2);
		}

		private Guid GetProjectionId(string name) {
			var field = _manager.GetType().GetField("_projectionsMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
			Guid projection1Id = GetProjectionId(Projection1);
			AllWritesSucceed(false);
			AllWritesQueueUp();
			WhenLoop(TestMessages(projection1Id));
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<Command.Post>().Count() + _queue.TimerMessagesOfType<Command.PostBatch>().Count() > 0) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0, _consumer.HandledMessages.OfType<OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection1, "dummy"));
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is NotFound);
			Assert.IsTrue(fakeEnvelope.Replies[1] is ProjectionState);
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
			foreach (var m in base.When())
				yield return m;
			yield return
				new Command.Post(_bus, ProjectionMode.Continuous,
					Projection1,
					RunAs.System, $"native:{FakeProjectionType.AssemblyQualifiedName}",
					ProjectionSource, enabled: true, checkpointsEnabled: true,
					emitEnabled: false, trackEmittedStreams: false);
			yield return
				new Command.Post(_bus, ProjectionMode.Continuous,
					Projection2,
					RunAs.System, $"native:{FakeProjectionType.AssemblyQualifiedName}",
					ProjectionSource, enabled: true, checkpointsEnabled: true,
					emitEnabled: false, trackEmittedStreams: false);
		}

		private static IEnumerable<WhenStep> TestMessages(Guid projectionToDeletedId1, Guid projectionToDeletedId2) {
			yield return new ProjectionManagementMessage.Internal.Deleted(Projection1, projectionToDeletedId1);
			yield return new ProjectionManagementMessage.Internal.Deleted(Projection2, projectionToDeletedId2);
		}

		private Guid GetProjectionId(string name) {
			var field = _manager.GetType().GetField("_projectionsMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
			Guid projection1Id = GetProjectionId(Projection1);
			Guid projection2Id = GetProjectionId(Projection2);
			AllWritesSucceed(false);
			AllWritesQueueUp();
			WhenLoop(TestMessages(projection1Id, projection2Id));
			AllWriteComplete();
			AllWritesSucceed();
			while (_queue.TimerMessagesOfType<ProjectionManagementMessage.Internal.Deleted>().Any()) {
				_queue.ProcessTimer();
				Thread.Sleep(100);
			}

			Process();

			Assert.AreEqual(0, _consumer.HandledMessages.OfType<OperationFailed>().Count());
			var fakeEnvelope = new FakeEnvelope();
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection1, "dummy"));
			_manager.Handle(new Command.GetState(fakeEnvelope, Projection2, "dummy"));

			Process();

			Assert.AreEqual(2, fakeEnvelope.Replies.Count);
			Assert.IsTrue(fakeEnvelope.Replies[0] is NotFound);
			Assert.IsTrue(fakeEnvelope.Replies[1] is NotFound);
		}
	}
}
