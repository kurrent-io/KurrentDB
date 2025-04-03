// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Phases;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;
using Serilog;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Tests.Services.core_projection.multi_phase;

abstract class specification_with_multi_phase_core_projection<TLogFormat, TStreamId> : TestFixtureWithCoreProjection<TLogFormat, TStreamId> {
	private FakeCheckpointManager _phase1checkpointManager;
	private FakeCheckpointManager _phase2checkpointManager;
	private IEmittedStreamsTracker _emittedStreamsTracker;
	private FakeProjectionProcessingPhase _phase1;
	private FakeProjectionProcessingPhase _phase2;
	private IReaderStrategy _phase1readerStrategy;
	private IReaderStrategy _phase2readerStrategy;

	class FakeProjectionProcessingStrategy : ProjectionProcessingStrategy {
		private readonly FakeProjectionProcessingPhase _phase1;
		private readonly FakeProjectionProcessingPhase _phase2;

		public FakeProjectionProcessingStrategy(
			string name, ProjectionVersion projectionVersion, ILogger logger, FakeProjectionProcessingPhase phase1,
			FakeProjectionProcessingPhase phase2)
			: base(name, projectionVersion, logger, Opts.MaxProjectionStateSizeDefault) {
			_phase1 = phase1;
			_phase2 = phase2;
		}

		protected override IQuerySources GetSourceDefinition() {
			return new QuerySourcesDefinition {
				AllStreams = true,
				AllEvents = true,
				ByStreams = true,
				Options = new QuerySourcesDefinitionOptions()
			};
		}

		public override bool GetStopOnEof() {
			return true;
		}

		public override bool GetUseCheckpoints() {
			return false;
		}

		public override bool GetRequiresRootPartition() {
			return false;
		}

		public override bool GetProducesRunningResults() {
			return true;
		}

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
			return new IProjectionProcessingPhase[] {_phase1, _phase2};
		}
	}

	internal class FakeProjectionProcessingPhase : IProjectionProcessingPhase {
		private readonly int _phase;
		private readonly specification_with_multi_phase_core_projection<TLogFormat, TStreamId> _specification;
		private readonly ICoreProjectionCheckpointManager _checkpointManager;
		private readonly IEmittedStreamsTracker _emittedStreamsTracker;
		private readonly IReaderStrategy _readerStrategy;

		private bool _initializedFromCheckpoint;
		private CheckpointTag _initializedFromCheckpointAt;
		private PhaseState _state;
		private Guid _subscriptionId;
		private int _subscribeInvoked;

		public FakeProjectionProcessingPhase(int phase,
			specification_with_multi_phase_core_projection<TLogFormat, TStreamId> specification,
			ICoreProjectionCheckpointManager checkpointManager, IReaderStrategy readerStrategy,
			IEmittedStreamsTracker emittedStreamsTracker) {
			_phase = phase;
			_specification = specification;
			_checkpointManager = checkpointManager;
			_readerStrategy = readerStrategy;
			_emittedStreamsTracker = emittedStreamsTracker;
		}

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
			_initializedFromCheckpoint = true;
			_initializedFromCheckpointAt = checkpointTag;
		}

		public void SetProjectionState(PhaseState state) {
			_state = state;
		}

		public void ProcessEvent() {
			ProcessEventInvoked++;
		}

		public void Subscribe(CheckpointTag from, bool fromCheckpoint) {
			_subscribeInvoked++;
			_subscriptionId = Guid.NewGuid();
			_specification._coreProjection.Subscribed();
		}

		public void EnsureUnsubscribed() {
			throw new NotImplementedException();
		}

		public CheckpointTag MakeZeroCheckpointTag() {
			return CheckpointTag.FromPhase(_phase, completed: false);
		}

		public ICoreProjectionCheckpointManager CheckpointManager {
			get { return _checkpointManager; }
		}

		public IEmittedStreamsTracker EmittedStreamsTracker {
			get { return _emittedStreamsTracker; }
		}

		public IReaderStrategy ReaderStrategy {
			get { return _readerStrategy; }
		}

		public bool InitializedFromCheckpoint {
			get { return _initializedFromCheckpoint; }
		}

		public CheckpointTag InitializedFromCheckpointAt {
			get { return _initializedFromCheckpointAt; }
		}

		public PhaseState State {
			get { return _state; }
		}

		public Guid SubscriptionId {
			get { return _subscriptionId; }
		}

		public bool Unsubscribed_ {
			get { return false; }
		}

		public int ProcessEventInvoked { get; set; }

		public int SubscribeInvoked {
			get { return _subscribeInvoked; }
			set { _subscribeInvoked = value; }
		}

		public void GetStatistics(ProjectionStatistics info) {
		}

		public void Complete() {
			_specification._coreProjection.CompletePhase();
		}
	}

	internal class FakeCheckpointManager : ICoreProjectionCheckpointManager, IEmittedEventWriter {
		private readonly IPublisher _publisher;
		private readonly Guid _projectionCorrelationId;

		private bool _started;
		private CheckpointTag _startedAt;
		private CheckpointTag _lastEvent;
		private float _progress;
		private bool _stopped;
		private bool _stopping;
		private readonly List<EmittedEventEnvelope> _emittedEvents = new List<EmittedEventEnvelope>();

		public FakeCheckpointManager(IPublisher publisher, Guid projectionCorrelationId) {
			_publisher = publisher;
			_projectionCorrelationId = projectionCorrelationId;
		}

		public void Initialize() {
		}

		public void Start(CheckpointTag checkpointTag, PartitionState rootPartitionState) {
			_started = true;
			_startedAt = checkpointTag;
			_lastEvent = checkpointTag;
		}

		public void Stopping() {
			_stopping = true;
			_publisher.Publish(
				new CoreProjectionProcessingMessage.CheckpointCompleted(_projectionCorrelationId, _lastEvent));
		}

		public void Stopped() {
			_stopped = true;
		}

		public void GetStatistics(ProjectionStatistics info) {
		}

		public void NewPartition(string partition, CheckpointTag eventCheckpointTag) {
			throw new NotImplementedException();
		}

		public void EventsEmitted(
			EmittedEventEnvelope[] scheduledWrites, Guid causedBy, string correlationId) {
			EmittedEvents.AddRange(scheduledWrites);
		}

		public void StateUpdated(string partition, PartitionState oldState, PartitionState newState) {
			throw new NotImplementedException();
		}

		public void PartitionCompleted(string partition) {
		}

		public void EventProcessed(CheckpointTag checkpointTag, float progress) {
			_lastEvent = checkpointTag;
		}

		public bool CheckpointSuggested(CheckpointTag checkpointTag, float progress) {
			throw new NotImplementedException();
		}

		public void Progress(float progress) {
			_progress = progress;
		}

		public void BeginLoadState() {
			_publisher.Publish(
				new CoreProjectionProcessingMessage.CheckpointLoaded(
					_projectionCorrelationId, CheckpointTag.FromPosition(0, 0, -1), "", 0));
		}

		public void BeginLoadPrerecordedEvents(CheckpointTag checkpointTag) {
			_publisher.Publish(
				new CoreProjectionProcessingMessage.PrerecordedEventsLoaded(_projectionCorrelationId,
					checkpointTag));
		}

		public void BeginLoadPartitionStateAt(string statePartition, CheckpointTag requestedStateCheckpointTag,
			Action<PartitionState> loadCompleted) {
			throw new NotImplementedException();
		}

		public void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag,
			Action committed) {
			throw new NotImplementedException();
		}

		public CheckpointTag LastProcessedEventPosition {
			get { return _lastEvent; }
		}

		public bool Started {
			get { return _started; }
		}

		public CheckpointTag StartedAt {
			get { return _startedAt; }
		}

		public float Progress_ {
			get { return _progress; }
		}

		public bool Stopped_ {
			get { return _stopped; }
		}

		public bool Stopping_ {
			get { return _stopping; }
		}

		public List<EmittedEventEnvelope> EmittedEvents {
			get { return _emittedEvents; }
		}
	}

	protected class FakeReaderStrategy : IReaderStrategy {
		private readonly int _phase;

		public FakeReaderStrategy(int phase) {
			_phase = phase;
		}

		public bool IsReadingOrderRepeatable {
			get { throw new NotImplementedException(); }
		}

		public EventFilter EventFilter {
			get { throw new NotImplementedException(); }
		}

		public PositionTagger PositionTagger {
			get { return new TransactionFilePositionTagger(Phase); }
		}

		public int Phase {
			get { return _phase; }
		}

		public IReaderSubscription CreateReaderSubscription(
			IPublisher publisher, CheckpointTag fromCheckpointTag, Guid subscriptionId,
			ReaderSubscriptionOptions readerSubscriptionOptions) {
			throw new NotImplementedException();
		}

		public IEventReader CreatePausedEventReader(
			Guid eventReaderId, IPublisher publisher, IODispatcher ioDispatcher, CheckpointTag checkpointTag,
			bool stopOnEof, int? stopAfterNEvents) {
			throw new NotImplementedException();
		}
	}

	public class FakeEmittedStreamsTracker : IEmittedStreamsTracker {
		public void Initialize() {
		}

		public void TrackEmittedStream(EmittedEvent[] emittedEvents) {
		}
	}

	public FakeCheckpointManager Phase1CheckpointManager {
		get { return _phase1checkpointManager; }
	}

	public FakeCheckpointManager Phase2CheckpointManager {
		get { return _phase2checkpointManager; }
	}


	public FakeProjectionProcessingPhase Phase1 {
		get { return _phase1; }
	}

	public FakeProjectionProcessingPhase Phase2 {
		get { return _phase2; }
	}

	protected override ProjectionProcessingStrategy GivenProjectionProcessingStrategy() {
		_phase1checkpointManager = new FakeCheckpointManager(_bus, _projectionCorrelationId);
		_phase2checkpointManager = new FakeCheckpointManager(_bus, _projectionCorrelationId);
		_emittedStreamsTracker = new FakeEmittedStreamsTracker();
		_phase1readerStrategy = GivenPhase1ReaderStrategy();
		_phase2readerStrategy = GivenPhase2ReaderStrategy();
		_phase1 = new FakeProjectionProcessingPhase(0, this, Phase1CheckpointManager, _phase1readerStrategy,
			_emittedStreamsTracker);
		_phase2 = new FakeProjectionProcessingPhase(1, this, Phase2CheckpointManager, _phase2readerStrategy,
			_emittedStreamsTracker);
		return new FakeProjectionProcessingStrategy(
			_projectionName, _version, Log.Logger, Phase1, Phase2);
	}

	protected virtual FakeReaderStrategy GivenPhase2ReaderStrategy() {
		return new FakeReaderStrategy(1);
	}

	protected virtual FakeReaderStrategy GivenPhase1ReaderStrategy() {
		return new FakeReaderStrategy(0);
	}
}
