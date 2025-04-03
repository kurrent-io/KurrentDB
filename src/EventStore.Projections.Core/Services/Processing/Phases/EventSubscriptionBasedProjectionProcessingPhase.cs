// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Contracts;
using System.Linq;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Settings;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messaging;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;
using EventStore.Projections.Core.Services.Processing.WorkItems;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public abstract partial class EventSubscriptionBasedProjectionProcessingPhase : IProjectionPhaseCompleter,
	IProjectionPhaseCheckpointManager,
	IHandle<EventReaderSubscriptionMessage.ProgressChanged>,
	IHandle<EventReaderSubscriptionMessage.SubscriptionStarted>,
	IHandle<EventReaderSubscriptionMessage.NotAuthorized>,
	IHandle<EventReaderSubscriptionMessage.EofReached>,
	IHandle<EventReaderSubscriptionMessage.CheckpointSuggested>,
	IHandle<EventReaderSubscriptionMessage.ReaderAssignedReader>,
	IHandle<EventReaderSubscriptionMessage.Failed>,
	IHandle<EventReaderSubscriptionMessage.SubscribeTimeout>,
	IProjectionProcessingPhase,
	IProjectionPhaseStateManager {
	protected readonly IPublisher _publisher;
	private readonly IPublisher _inputQueue;
	protected readonly ICoreProjectionForProcessingPhase _coreProjection;
	protected readonly Guid _projectionCorrelationId;
	protected readonly ICoreProjectionCheckpointManager _checkpointManager;
	protected readonly IProgressResultWriter _progressResultWriter;
	protected readonly ProjectionConfig _projectionConfig;
	protected readonly string _projectionName;
	protected readonly Serilog.ILogger _logger;
	protected readonly CheckpointTag _zeroCheckpointTag;
	protected readonly CoreProjectionQueue _processingQueue;
	protected readonly PartitionStateCache _partitionStateCache;
	protected readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;
	protected readonly IReaderStrategy _readerStrategy;
	protected readonly IResultWriter _resultWriter;
	protected readonly bool _useCheckpoints;
	protected long _expectedSubscriptionMessageSequenceNumber = -1;
	protected Guid _currentSubscriptionId;
	protected PhaseSubscriptionState _subscriptionState;
	protected PhaseState _state;
	protected readonly bool _stopOnEof;
	private readonly bool _isBiState;
	protected readonly IEmittedStreamsTracker _emittedStreamsTracker;
	protected readonly bool _enableContentTypeValidation;

	private readonly Action _updateStatistics;

	protected EventSubscriptionBasedProjectionProcessingPhase(
		IPublisher publisher,
		IPublisher inputQueue,
		ICoreProjectionForProcessingPhase coreProjection,
		Guid projectionCorrelationId,
		ICoreProjectionCheckpointManager checkpointManager,
		ProjectionConfig projectionConfig,
		string projectionName,
		ILogger logger,
		CheckpointTag zeroCheckpointTag,
		PartitionStateCache partitionStateCache,
		IResultWriter resultWriter,
		Action updateStatistics,
		ReaderSubscriptionDispatcher subscriptionDispatcher,
		IReaderStrategy readerStrategy,
		bool useCheckpoints,
		bool stopOnEof,
		bool orderedPartitionProcessing,
		bool isBiState,
		IEmittedStreamsTracker emittedStreamsTracker,
		bool enableContentTypeValidation) {
		_publisher = publisher;
		_inputQueue = inputQueue;
		_coreProjection = coreProjection;
		_projectionCorrelationId = projectionCorrelationId;
		_checkpointManager = checkpointManager;
		_projectionConfig = projectionConfig;
		_projectionName = projectionName;
		_logger = logger;
		_zeroCheckpointTag = zeroCheckpointTag;
		_partitionStateCache = partitionStateCache;
		_resultWriter = resultWriter;
		_updateStatistics = updateStatistics;
		_processingQueue = new CoreProjectionQueue(publisher,
			projectionConfig.PendingEventsThreshold,
			orderedPartitionProcessing);
		_processingQueue.EnsureTickPending += EnsureTickPending;
		_subscriptionDispatcher = subscriptionDispatcher;
		_readerStrategy = readerStrategy;
		_useCheckpoints = useCheckpoints;
		_stopOnEof = stopOnEof;
		_isBiState = isBiState;
		_progressResultWriter = new ProgressResultWriter(this, _resultWriter);
		_inutQueueEnvelope = _inputQueue;
		_emittedStreamsTracker = emittedStreamsTracker;
		_enableContentTypeValidation = enableContentTypeValidation;
	}

	public void UnlockAndForgetBefore(CheckpointTag checkpointTag) {
		_partitionStateCache.Unlock(checkpointTag, forgetUnlocked: true);
	}

	public CheckpointTag LastProcessedEventPosition {
		get { return _coreProjection.LastProcessedEventPosition; }
	}

	public ICoreProjectionCheckpointManager CheckpointManager {
		get { return _checkpointManager; }
	}

	public IEmittedStreamsTracker EmittedStreamsTracker {
		get { return _emittedStreamsTracker; }
	}

	protected bool IsOutOfOrderSubscriptionMessage(EventReaderSubscriptionMessageBase message) {
		if (_currentSubscriptionId != message.SubscriptionId)
			return true;
		if (_expectedSubscriptionMessageSequenceNumber != message.SubscriptionMessageSequenceNumber)
			throw new InvalidOperationException("Out of order message detected");
		return false;
	}

	protected void RegisterSubscriptionMessage(EventReaderSubscriptionMessageBase message) {
		_expectedSubscriptionMessageSequenceNumber = message.SubscriptionMessageSequenceNumber + 1;
	}

	protected void EnsureTickPending() {
		_coreProjection.EnsureTickPending();
	}

	public void ProcessEvent() {
		_processingQueue.ProcessEvent();
		EnsureUpdateStatisticksTickPending();
	}

	private void EnsureUpdateStatisticksTickPending() {
		if (_updateStatisticsTicketPending)
			return;
		_updateStatisticsTicketPending = true;
		_publisher.Publish(
			TimerMessage.Schedule.Create(
				_updateInterval,
				_inutQueueEnvelope,
				new UnwrapEnvelopeMessage(MarkTicketReceivedAndUpdateStatistics)));
	}

	private void MarkTicketReceivedAndUpdateStatistics() {
		_updateStatisticsTicketPending = false;
		UpdateStatistics();
	}

	private void UpdateStatistics() {
		if (_updateStatistics != null)
			_updateStatistics();
	}

	public void Handle(EventReaderSubscriptionMessage.ProgressChanged message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			var progressWorkItem =
				new ProgressWorkItem(_checkpointManager, _progressResultWriter, message.Progress);
			_processingQueue.EnqueueTask(progressWorkItem, message.CheckpointTag, allowCurrentPosition: true);
			ProcessEvent();
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.SubscriptionStarted message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			_subscriptionStartedAtLastCommitPosition = message.StartingLastCommitPosition;
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.NotAuthorized message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			var progressWorkItem = new NotAuthorizedWorkItem();
			_processingQueue.EnqueueTask(progressWorkItem, message.CheckpointTag, allowCurrentPosition: true);
			ProcessEvent();
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Unsubscribed() {
		_subscriptionDispatcher.Cancel(_projectionCorrelationId);
		_subscriptionState = PhaseSubscriptionState.Unsubscribed;
		_processingQueue.Unsubscribed();
	}

	public void Handle(EventReaderSubscriptionMessage.EofReached message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			Unsubscribed();
			var completedWorkItem = new CompletedWorkItem(this);
			_processingQueue.EnqueueTask(completedWorkItem, message.CheckpointTag, allowCurrentPosition: true);
			ProcessEvent();
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.CheckpointSuggested message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			if (_useCheckpoints) {
				CheckpointTag checkpointTag = message.CheckpointTag;
				var checkpointSuggestedWorkItem =
					new CheckpointSuggestedWorkItem(this, message, _checkpointManager);
				_processingQueue.EnqueueTask(checkpointSuggestedWorkItem, checkpointTag,
					allowCurrentPosition: true);
			}

			ProcessEvent();
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(CoreProjectionManagementMessage.GetState message) {
		try {
			var getStateWorkItem = new GetStateWorkItem(
				_publisher, message.CorrelationId, message.ProjectionId, this, message.Partition);
			_processingQueue.EnqueueOutOfOrderTask(getStateWorkItem);
			ProcessEvent();
		} catch (Exception ex) {
			_publisher.Publish(
				new CoreProjectionStatusMessage.StateReport(
					message.CorrelationId, _projectionCorrelationId, message.Partition, state: null,
					position: null));
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(CoreProjectionManagementMessage.GetResult message) {
		try {
			var getResultWorkItem = new GetResultWorkItem(
				_publisher, message.CorrelationId, message.ProjectionId, this, message.Partition);
			_processingQueue.EnqueueOutOfOrderTask(getResultWorkItem);
			ProcessEvent();
		} catch (Exception ex) {
			_publisher.Publish(
				new CoreProjectionStatusMessage.ResultReport(
					message.CorrelationId, _projectionCorrelationId, message.Partition, result: null,
					position: null));
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.SubscribeTimeout message) {
		if (_subscriptionState is not PhaseSubscriptionState.Subscribing
		    || message.SubscriptionId != _currentSubscriptionId) return;
		SubscriptionFailed("Reader subscription timed out");
	}

	public void Handle(EventReaderSubscriptionMessage.Failed message) => SubscriptionFailed(message.Reason);

	private void SubscriptionFailed(string reason) {
		if (_subscriptionState is PhaseSubscriptionState.Subscribed or PhaseSubscriptionState.Subscribing)
			_subscriptionDispatcher.Cancel(_currentSubscriptionId);
		_subscriptionState = PhaseSubscriptionState.Failed;
		_coreProjection.SetFaulted(reason);
	}

	protected void UnsubscribeFromPreRecordedOrderEvents() {
		// projectionCorrelationId is used as a subscription identifier for delivery
		// of pre-recorded order events recovered by checkpoint manager
		_subscriptionDispatcher.Cancel(_projectionCorrelationId);
		_subscriptionState = PhaseSubscriptionState.Unsubscribed;
	}

	public void Subscribed(Guid subscriptionId) {
		_processingQueue.Subscribed(subscriptionId);
	}

	public ReaderSubscriptionOptions GetSubscriptionOptions() {
		return new ReaderSubscriptionOptions(
			_projectionConfig.CheckpointUnhandledBytesThreshold, _projectionConfig.CheckpointHandledThreshold,
			_projectionConfig.CheckpointAfterMs,
			_stopOnEof, stopAfterNEvents: null, _enableContentTypeValidation);
	}

	protected void SubscribeReaders(CheckpointTag checkpointTag) {
		_expectedSubscriptionMessageSequenceNumber = 0;
		_currentSubscriptionId = Guid.NewGuid();
		Subscribed(_currentSubscriptionId);
		var readerStrategy = _readerStrategy;
		if (readerStrategy != null) {
			_subscriptionState = PhaseSubscriptionState.Subscribing;
			_subscriptionDispatcher.PublishSubscribe(
				new ReaderSubscriptionManagement.Subscribe(
					_currentSubscriptionId, checkpointTag, readerStrategy,
					GetSubscriptionOptions()), this, scheduleTimeout: true);
		} else {
			_coreProjection.Subscribed();
		}
	}

	public void SubscribeToPreRecordedOrderEvents() {
		var coreProjection = (CoreProjection)_coreProjection;
		// projectionCorrelationId is used as a subscription identifier for delivery
		// of pre-recorded order events recovered by checkpoint manager
		_expectedSubscriptionMessageSequenceNumber = 0;
		_currentSubscriptionId = coreProjection._projectionCorrelationId;
		_subscriptionDispatcher.Subscribed(coreProjection._projectionCorrelationId, this);
		// even if it is not a real subscription we need to unsubscribe
		_subscriptionState = PhaseSubscriptionState.Subscribed;
	}

	public virtual void Subscribe(CheckpointTag from, bool fromCheckpoint) {
		Contract.Assert(_checkpointManager.LastProcessedEventPosition == @from);
		if (fromCheckpoint) {
			SubscribeToPreRecordedOrderEvents();
			_checkpointManager.BeginLoadPrerecordedEvents(@from);
		} else
			SubscribeReaders(@from);
	}

	public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
		UnsubscribeFromPreRecordedOrderEvents();
		SubscribeReaders(message.CheckpointTag);
	}

	public CheckpointTag AdjustTag(CheckpointTag tag) {
		return _readerStrategy.PositionTagger.AdjustTag(tag);
	}

	protected void SetFaulting(string faultedReason, Exception ex = null) {
		if (_logger != null) {
			if (ex != null)
				_logger.Error(ex, faultedReason);
			else
				_logger.Error(faultedReason);
		}

		_coreProjection.SetFaulting(faultedReason);
	}

	protected bool ValidateEmittedEvents(EmittedEventEnvelope[] emittedEvents) {
		if (!_projectionConfig.EmitEventEnabled) {
			if (emittedEvents != null && emittedEvents.Length > 0) {
				SetFaulting("'emit' is not allowed by the projection/configuration/mode");
				return false;
			}
		}

		return true;
	}

	public abstract void NewCheckpointStarted(CheckpointTag at);

	public void InitializeFromCheckpoint(CheckpointTag checkpointTag) {
		_subscriptionState = PhaseSubscriptionState.Unknown;
		// this can be old checkpoint
		var adjustedCheckpointTag = _readerStrategy.PositionTagger.AdjustTag(checkpointTag);
		_processingQueue.InitializeQueue(adjustedCheckpointTag);
	}

	public int GetBufferedEventCount() {
		return _processingQueue.GetBufferedEventCount();
	}

	public string GetStatus() {
		return _processingQueue.GetStatus();
	}

	protected EventProcessedResult InternalCommittedEventProcessed(
		string partition, EventReaderSubscriptionMessage.CommittedEventReceived message,
		EmittedEventEnvelope[] emittedEvents, PartitionState newPartitionState,
		PartitionState newSharedPartitionState) {
		if (_subscriptionState != PhaseSubscriptionState.Subscribed)
			_logger?.Verbose("Got CommittedEventReceived in {state} SubscriptionState, but expected to be in {expectedState}",
				_subscriptionState, PhaseSubscriptionState.Subscribed);

		if (!ValidateEmittedEvents(emittedEvents))
			return null;

		bool eventsWereEmitted = emittedEvents != null;
		var oldState = _partitionStateCache.GetLockedPartitionState(partition);
		var oldSharedState = _isBiState ? _partitionStateCache.GetLockedPartitionState("") : null;
		bool changed = oldState.IsChanged(newPartitionState)
		               || (_isBiState && oldSharedState.IsChanged(newSharedPartitionState));

		PartitionState partitionState = null;
		// NOTE: projectionResult cannot change independently unless projection definition has changed
		if (changed) {
			var lockPartitionStateAt = partition != "" ? message.CheckpointTag : null;
			partitionState = newPartitionState;
			_partitionStateCache.CacheAndLockPartitionState(partition, partitionState, lockPartitionStateAt);
			if (_isBiState) {
				_partitionStateCache.CacheAndLockPartitionState("", newSharedPartitionState, null);
			}
		}

		if (changed || eventsWereEmitted) {
			var correlationId =
				message.Data.IsJson ? message.Data.Metadata.ParseCheckpointTagCorrelationId() : null;
			return new EventProcessedResult(
				partition, message.CheckpointTag, oldState, partitionState, oldSharedState, newSharedPartitionState,
				emittedEvents, message.Data.EventId, correlationId);
		} else return null;
	}

	protected EventProcessedResult InternalPartitionDeletedProcessed(
		string partition, CheckpointTag deletePosition,
		PartitionState newPartitionState
	) {
		var oldState = _partitionStateCache.GetLockedPartitionState(partition);
		var oldSharedState = _isBiState ? _partitionStateCache.GetLockedPartitionState("") : null;
		bool changed = oldState.IsChanged(newPartitionState);


		PartitionState partitionState = null;
		// NOTE: projectionResult cannot change independently unless projection definition has changed
		if (changed) {
			var lockPartitionStateAt = partition != "" ? deletePosition : null;
			partitionState = newPartitionState;
			_partitionStateCache.CacheAndLockPartitionState(partition, partitionState, lockPartitionStateAt);
		}

		return new EventProcessedResult(
			partition, deletePosition, oldState, partitionState, oldSharedState, null, null, Guid.Empty, null,
			isPartitionTombstone: true);
	}

	public void BeginGetPartitionStateAt(
		string statePartition, CheckpointTag at, Action<PartitionState> loadCompleted, bool lockLoaded) {
		if (statePartition == "") // root is always cached
		{
			// root partition is always locked
			var state = _partitionStateCache.TryGetAndLockPartitionState(statePartition, null);
			loadCompleted(state);
		} else {
			var s = lockLoaded
				? _partitionStateCache.TryGetAndLockPartitionState(statePartition, at)
				: _partitionStateCache.TryGetPartitionState(statePartition);
			if (s != null)
				loadCompleted(s);
			else {
				Action<PartitionState> completed = state => {
					if (lockLoaded)
						_partitionStateCache.CacheAndLockPartitionState(statePartition, state, at);
					else
						_partitionStateCache.CachePartitionState(statePartition, state);
					loadCompleted(state);
				};
				if (_projectionConfig.CheckpointsEnabled) {
					_checkpointManager.BeginLoadPartitionStateAt(statePartition, at, completed);
				} else {
					var state = new PartitionState("", null, _zeroCheckpointTag);
					completed(state);
				}
			}
		}
	}

	public void FinalizeEventProcessing(
		EventProcessedResult result, CheckpointTag eventCheckpointTag, float progress) {
		if (_state == PhaseState.Running) {
			//TODO: move to separate projection method and cache result in work item
			if (result != null) {
				_resultWriter.AccountPartition(result);
				if (_projectionConfig.EmitEventEnabled && result.EmittedEvents != null) {
					_resultWriter.EventsEmitted(
						result.EmittedEvents, result.CausedBy, result.CorrelationId);
					_emittedStreamsTracker.TrackEmittedStream(result.EmittedEvents.Select(x => x.Event).ToArray());
				}

				if (result.NewState != null) {
					_resultWriter.WriteRunningResult(result);
					_checkpointManager.StateUpdated(result.Partition, result.OldState, result.NewState);
				}

				if (result.NewSharedState != null) {
					_checkpointManager.StateUpdated("", result.OldSharedState, result.NewSharedState);
				}
			}

			_checkpointManager.EventProcessed(eventCheckpointTag, progress);
			_progressResultWriter.WriteProgress(progress);
		}
	}

	public void EmitEofResult(
		string partition, string resultBody, CheckpointTag causedBy, Guid causedByGuid, string correlationId) {
		_resultWriter.WriteEofResult(
			_currentSubscriptionId, partition, resultBody, causedBy, causedByGuid, correlationId);
	}

	public void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action completed) {
		switch (_state) {
			case PhaseState.Running:
				_checkpointManager.RecordEventOrder(
					resolvedEvent, orderCheckpointTag, completed);
				break;
			case PhaseState.Stopped:
				_logger.Error("Should not receive events in stopped state anymore");
				completed(); // allow collecting events for debugging
				break;
		}
	}

	public void Complete() {
		//NOTE: no need for EnsureUnsubscribed  as EOF
		Unsubscribed();
		_coreProjection.CompletePhase();
	}

	public void SetCurrentCheckpointSuggestedWorkItem(CheckpointSuggestedWorkItem checkpointSuggestedWorkItem) {
		_coreProjection.SetCurrentCheckpointSuggestedWorkItem(checkpointSuggestedWorkItem);
	}

	public virtual void GetStatistics(ProjectionStatistics info) {
		info.Status = info.Status + GetStatus();
		info.BufferedEvents += GetBufferedEventCount();
	}

	public CheckpointTag MakeZeroCheckpointTag() {
		return _zeroCheckpointTag;
	}

	public void EnsureUnsubscribed() {
		if (_subscriptionState is PhaseSubscriptionState.Subscribed) {
			Unsubscribed();
			// this way we distinguish pre-recorded events subscription
			if (_currentSubscriptionId != _projectionCorrelationId)
				_publisher.Publish(
					new ReaderSubscriptionManagement.Unsubscribe(_currentSubscriptionId));
		}
	}

	protected long _subscriptionStartedAtLastCommitPosition;
	private readonly IEnvelope _inutQueueEnvelope;
	private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(250);
	private bool _updateStatisticsTicketPending;

	public void Handle(EventReaderSubscriptionMessage.ReaderAssignedReader message) {
		if (_state != PhaseState.Starting)
			return;
		if (_subscriptionState is not PhaseSubscriptionState.Subscribing
		    || message.SubscriptionId != _currentSubscriptionId)
			return;
		_subscriptionState = PhaseSubscriptionState.Subscribed;
		_coreProjection.Subscribed();
	}

	public abstract void Dispose();

	public void SetProjectionState(PhaseState state) {
		var starting = _state == PhaseState.Starting && state == PhaseState.Running;

		_state = state;
		_processingQueue.SetIsRunning(state == PhaseState.Running);
		if (starting)
			NewCheckpointStarted(LastProcessedEventPosition);
	}
}
