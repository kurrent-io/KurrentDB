// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.multi_phase;

abstract class
	specification_with_multi_phase_core_projection<TLogFormat, TStreamId> : TestFixtureWithCoreProjection<TLogFormat, TStreamId> {
	private IEmittedStreamsTracker _emittedStreamsTracker;

	private class FakeProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		ILogger logger,
		FakeProjectionProcessingPhase phase1,
		FakeProjectionProcessingPhase phase2)
		: ProjectionProcessingStrategy(name, projectionVersion, logger, Opts.MaxProjectionStateSizeDefault) {
		protected override IQuerySources GetSourceDefinition()
			=> new QuerySourcesDefinition {
				AllStreams = true,
				AllEvents = true,
				ByStreams = true,
				Options = new()
			};

		public override bool GetStopOnEof() => true;

		public override bool GetUseCheckpoints() => false;

		public override bool GetRequiresRootPartition() => false;

		protected override bool GetProducesRunningResults() => true;

		public override void EnrichStatistics(ProjectionStatistics info) {
		}

		public override IProjectionProcessingPhase[] CreateProcessingPhases(
			IPublisher publisher,
			IPublisher inputQueue,
			Guid projectionCorrelationId,
			PartitionStateCache partitionStateCache,
			Action updateStatistics,
			CoreProjection coreProjection,
			ProjectionNamesBuilder namingBuilder,
			ITimeProvider timeProvider,
			IODispatcher ioDispatcher,
			CoreProjectionCheckpointWriter coreProjectionCheckpointWriter) {
			return [phase1, phase2];
		}
	}

	internal class FakeProjectionProcessingPhase(
		int phase,
		specification_with_multi_phase_core_projection<TLogFormat, TStreamId> specification,
		ICoreProjectionCheckpointManager checkpointManager,
		IEmittedStreamsTracker emittedStreamsTracker)
		: IProjectionProcessingPhase {
		public void Dispose() {
			throw new NotImplementedException();
		}

		public void Handle(CoreProjectionManagementMessage.GetState message) {
			throw new NotImplementedException();
		}

		public void Handle(CoreProjectionManagementMessage.GetResult message) {
			throw new NotImplementedException();
		}

		public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
			throw new NotImplementedException();
		}

		public CheckpointTag AdjustTag(CheckpointTag tag) {
			return tag;
		}

		public void InitializeFromCheckpoint(CheckpointTag checkpointTag) {
			InitializedFromCheckpoint = true;
		}

		public void SetProjectionState(PhaseState state) {
		}

		public void ProcessEvent() {
			ProcessEventInvoked++;
		}

		public void Subscribe(CheckpointTag from, bool fromCheckpoint) {
			Guid.NewGuid();
			specification._coreProjection.Subscribed();
		}

		public void EnsureUnsubscribed() {
			throw new NotImplementedException();
		}

		public CheckpointTag MakeZeroCheckpointTag() => CheckpointTag.FromPhase(phase, completed: false);

		public ICoreProjectionCheckpointManager CheckpointManager { get; } = checkpointManager;

		public IEmittedStreamsTracker EmittedStreamsTracker { get; } = emittedStreamsTracker;

		public bool InitializedFromCheckpoint { get; private set; }

		public int ProcessEventInvoked { get; set; }

		public int SubscribeInvoked { get => 1; }

		public void GetStatistics(ProjectionStatistics info) {
		}

		public void Complete() {
			specification._coreProjection.CompletePhase();
		}
	}

	internal class FakeCheckpointManager(IPublisher publisher, Guid projectionCorrelationId)
		: ICoreProjectionCheckpointManager, IEmittedEventWriter {
		public void Initialize() {
		}

		public void Start(CheckpointTag checkpointTag, PartitionState rootPartitionState) {
			LastProcessedEventPosition = checkpointTag;
		}

		public void Stopping() {
			publisher.Publish(new CoreProjectionProcessingMessage.CheckpointCompleted(projectionCorrelationId, LastProcessedEventPosition));
		}

		public void Stopped() {
			Stopped_ = true;
		}

		public void GetStatistics(ProjectionStatistics info) {
		}

		public void EventsEmitted(
			EmittedEventEnvelope[] scheduledWrites,
			Guid causedBy,
			string correlationId) {
			EmittedEvents.AddRange(scheduledWrites);
		}

		public void StateUpdated(string partition, PartitionState oldState, PartitionState newState) {
			throw new NotImplementedException();
		}

		public void EventProcessed(CheckpointTag checkpointTag, float progress) {
			LastProcessedEventPosition = checkpointTag;
		}

		public bool CheckpointSuggested(CheckpointTag checkpointTag, float progress) {
			throw new NotImplementedException();
		}

		public void Progress(float progress) {
		}

		public void BeginLoadPrerecordedEvents(CheckpointTag checkpointTag) {
			publisher.Publish(new CoreProjectionProcessingMessage.PrerecordedEventsLoaded(projectionCorrelationId, checkpointTag));
		}

		public void BeginLoadPartitionStateAt(string statePartition,
			CheckpointTag requestedStateCheckpointTag,
			Action<PartitionState> loadCompleted) {
			throw new NotImplementedException();
		}

		public void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action committed) {
			throw new NotImplementedException();
		}

		public CheckpointTag LastProcessedEventPosition { get; private set; }

		public bool Stopped_ { get; private set; }

		public List<EmittedEventEnvelope> EmittedEvents { get; } = [];
	}

	protected class FakeReaderStrategy(int phase) : IReaderStrategy {
		public bool IsReadingOrderRepeatable => throw new NotImplementedException();

		public EventFilter EventFilter => throw new NotImplementedException();

		public PositionTagger PositionTagger => new TransactionFilePositionTagger(Phase);

		private int Phase => phase;

		public IReaderSubscription CreateReaderSubscription(
			IPublisher publisher,
			CheckpointTag fromCheckpointTag,
			Guid subscriptionId,
			ReaderSubscriptionOptions readerSubscriptionOptions) {
			throw new NotImplementedException();
		}

		public IEventReader CreatePausedEventReader(
			Guid eventReaderId,
			IPublisher publisher,
			CheckpointTag checkpointTag,
			bool stopOnEof) {
			throw new NotImplementedException();
		}
	}

	public class FakeEmittedStreamsTracker : IEmittedStreamsTracker {
		public void Initialize() {
		}

		public void TrackEmittedStream(EmittedEvent[] emittedEvents) {
		}
	}

	protected FakeCheckpointManager Phase1CheckpointManager { get; private set; }
	private FakeCheckpointManager Phase2CheckpointManager { get; set; }
	protected FakeProjectionProcessingPhase Phase1 { get; private set; }
	protected FakeProjectionProcessingPhase Phase2 { get; private set; }

	protected override ProjectionProcessingStrategy GivenProjectionProcessingStrategy() {
		Phase1CheckpointManager = new(_bus, _projectionCorrelationId);
		Phase2CheckpointManager = new(_bus, _projectionCorrelationId);
		_emittedStreamsTracker = new FakeEmittedStreamsTracker();
		Phase1 = new(0, this, Phase1CheckpointManager, _emittedStreamsTracker);
		Phase2 = new(1, this, Phase2CheckpointManager, _emittedStreamsTracker);
		return new FakeProjectionProcessingStrategy(_projectionName, _version, Log.Logger, Phase1, Phase2);
	}
}
