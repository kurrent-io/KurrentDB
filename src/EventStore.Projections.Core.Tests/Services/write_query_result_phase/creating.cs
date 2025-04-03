// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Bus;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Phases;
using EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager;
using EventStore.Projections.Core.Tests.Services.core_projection.multi_phase;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.write_query_result_phase {
	namespace creating {
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_creating<TLogFormat, TStreamId> {
			[Test]
			public void it_can_be_created() {
				var coreProjection = new FakeCoreProjection();
				var stateCache = new PartitionStateCache();
				var bus = new SynchronousScheduler();
				var fakeCheckpointManager =
					new specification_with_multi_phase_core_projection<TLogFormat, TStreamId>.FakeCheckpointManager(bus, Guid.NewGuid());
				var fakeEmittedStreamsTracker =
					new specification_with_multi_phase_core_projection<TLogFormat, TStreamId>.FakeEmittedStreamsTracker();
				TestHelper.Consume(
					new WriteQueryResultProjectionProcessingPhase(
						bus,
						1,
						"result-stream",
						coreProjection,
						stateCache,
						fakeCheckpointManager,
						fakeCheckpointManager,
						fakeEmittedStreamsTracker));
			}
		}

		abstract class specification_with_write_query_result_projection_processing_phase<TLogFormat, TStreamId> {
			protected WriteQueryResultProjectionProcessingPhase _phase;
			protected specification_with_multi_phase_core_projection<TLogFormat, TStreamId>.FakeCheckpointManager _checkpointManager;
			protected specification_with_multi_phase_core_projection<TLogFormat, TStreamId>.FakeEmittedStreamsTracker _emittedStreamsTracker;
			protected SynchronousScheduler _publisher;
			protected PartitionStateCache _stateCache;
			protected string _resultStreamName;
			protected FakeCoreProjection _coreProjection;

			[SetUp]
			public void SetUp() {
				_stateCache = GivenStateCache();
				_publisher = new();
				_coreProjection = new FakeCoreProjection();
				_checkpointManager = new specification_with_multi_phase_core_projection<TLogFormat, TStreamId>.FakeCheckpointManager(
					_publisher, Guid.NewGuid());
				_emittedStreamsTracker = new specification_with_multi_phase_core_projection<TLogFormat, TStreamId>.FakeEmittedStreamsTracker();
				_resultStreamName = "result-stream";
				_phase = new WriteQueryResultProjectionProcessingPhase(
					_publisher,
					1,
					_resultStreamName,
					_coreProjection,
					_stateCache,
					_checkpointManager,
					_checkpointManager,
					_emittedStreamsTracker);
				When();
			}

			protected virtual PartitionStateCache GivenStateCache() {
				var stateCache = new PartitionStateCache();

				stateCache.CachePartitionState(
					"a", new PartitionState("{}", null, CheckpointTag.FromPhase(0, completed: false)));
				stateCache.CachePartitionState(
					"b", new PartitionState("{}", null, CheckpointTag.FromPhase(0, completed: false)));
				stateCache.CachePartitionState(
					"c", new PartitionState("{}", null, CheckpointTag.FromPhase(0, completed: false)));
				return stateCache;
			}

			protected abstract void When();

			[TearDown]
			public void TearDown() {
				_phase = null;
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_created<TLogFormat, TStreamId> : specification_with_write_query_result_projection_processing_phase<TLogFormat, TStreamId> {
			protected override void When() {
			}

			[Test]
			public void can_be_initialized_from_phase_checkpoint() {
				_phase.InitializeFromCheckpoint(CheckpointTag.FromPhase(1, completed: false));
			}

			[Test]
			public void process_event_throws_invalid_operation_exception() {
				Assert.Throws<InvalidOperationException>(() => { _phase.ProcessEvent(); });
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_subscribing<TLogFormat, TStreamId> : specification_with_write_query_result_projection_processing_phase<TLogFormat, TStreamId> {
			protected override void When() {
				_phase.Subscribe(CheckpointTag.FromPhase(1, completed: false), false);
			}

			[Test]
			public void notifies_core_projection_with_subscribed() {
				Assert.AreEqual(1, _coreProjection.SubscribedInvoked);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_processing_event<TLogFormat, TStreamId> : specification_with_write_query_result_projection_processing_phase<TLogFormat, TStreamId> {
			protected override void When() {
				_phase.Subscribe(CheckpointTag.FromPhase(1, completed: false), false);
				_phase.SetProjectionState(PhaseState.Running);
				_phase.ProcessEvent();
			}

			[Test]
			public void writes_query_results() {
				Assert.AreEqual(3, _checkpointManager.EmittedEvents.Count(v => v.Event.EventType == "Result"));
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class
			when_completed_query_processing_event<TLogFormat, TStreamId> : specification_with_write_query_result_projection_processing_phase<TLogFormat, TStreamId> {
			protected override void When() {
				_phase.Subscribe(CheckpointTag.FromPhase(1, completed: false), false);
				_phase.SetProjectionState(PhaseState.Running);
				_phase.ProcessEvent();
				_phase.SetProjectionState(PhaseState.Stopped);
				_phase.ProcessEvent();
			}

			[Test]
			public void writes_query_results_only_once() {
				Assert.AreEqual(3, _checkpointManager.EmittedEvents.Count(v => v.Event.EventType == "Result"));
			}
		}
	}
}
