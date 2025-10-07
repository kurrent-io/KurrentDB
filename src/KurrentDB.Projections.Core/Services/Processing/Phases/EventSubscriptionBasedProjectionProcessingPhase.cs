// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Contracts;
using System.Linq;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using KurrentDB.Projections.Core.Services.Processing.WorkItems;
using ILogger = Serilog.ILogger;
using UnwrapEnvelopeMessage = KurrentDB.Projections.Core.Messaging.UnwrapEnvelopeMessage;

namespace KurrentDB.Projections.Core.Services.Processing.Phases;

public abstract class EventSubscriptionBasedProjectionProcessingPhase
	: IProjectionPhaseCompleter,
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
	private readonly IPublisher _publisher;
	protected readonly ICoreProjectionForProcessingPhase _coreProjection;
	private readonly Guid _projectionCorrelationId;
	private readonly ProjectionConfig _projectionConfig;
	protected readonly string _projectionName;
	protected readonly ILogger _logger;
	private readonly CheckpointTag _zeroCheckpointTag;
	protected readonly CoreProjectionQueue _processingQueue;
	protected readonly PartitionStateCache _partitionStateCache;
	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;
	private readonly IReaderStrategy _readerStrategy;
	protected readonly IResultWriter _resultWriter;
	private readonly bool _useCheckpoints;
	private long _expectedSubscriptionMessageSequenceNumber = -1;
	private Guid _currentSubscriptionId;
	private PhaseSubscriptionState _subscriptionState;
	protected PhaseState _state;
	private readonly bool _stopOnEof;
	private readonly bool _isBiState;
	private readonly bool _enableContentTypeValidation;

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
		_coreProjection = coreProjection;
		_projectionCorrelationId = projectionCorrelationId;
		CheckpointManager = checkpointManager;
		_projectionConfig = projectionConfig;
		_projectionName = projectionName;
		_logger = logger;
		_zeroCheckpointTag = zeroCheckpointTag;
		_partitionStateCache = partitionStateCache;
		_resultWriter = resultWriter;
		_updateStatistics = updateStatistics;
		_processingQueue = new(publisher, projectionConfig.PendingEventsThreshold, orderedPartitionProcessing);
		_processingQueue.EnsureTickPending += EnsureTickPending;
		_subscriptionDispatcher = subscriptionDispatcher;
		_readerStrategy = readerStrategy;
		_useCheckpoints = useCheckpoints;
		_stopOnEof = stopOnEof;
		_isBiState = isBiState;
		_inputQueueEnvelope = inputQueue;
		EmittedStreamsTracker = emittedStreamsTracker;
		_enableContentTypeValidation = enableContentTypeValidation;
	}

	public CheckpointTag LastProcessedEventPosition => _coreProjection.LastProcessedEventPosition;

	public ICoreProjectionCheckpointManager CheckpointManager { get; }

	public IEmittedStreamsTracker EmittedStreamsTracker { get; }

	protected bool IsOutOfOrderSubscriptionMessage(EventReaderSubscriptionMessageBase message) {
		if (_currentSubscriptionId != message.SubscriptionId)
			return true;
		return _expectedSubscriptionMessageSequenceNumber != message.SubscriptionMessageSequenceNumber
			? throw new InvalidOperationException("Out of order message detected")
			: false;
	}

	protected void RegisterSubscriptionMessage(EventReaderSubscriptionMessageBase message) {
		_expectedSubscriptionMessageSequenceNumber = message.SubscriptionMessageSequenceNumber + 1;
	}

	protected void EnsureTickPending() {
		_coreProjection.EnsureTickPending();
	}

	public void ProcessEvent() {
		_processingQueue.ProcessEvent();
		EnsureUpdateStatisticsTickPending();
	}

	private void EnsureUpdateStatisticsTickPending() {
		if (_updateStatisticsTicketPending)
			return;
		_updateStatisticsTicketPending = true;
		_publisher.Publish(
			TimerMessage.Schedule.Create(
				_updateInterval,
				_inputQueueEnvelope,
				new UnwrapEnvelopeMessage(MarkTicketReceivedAndUpdateStatistics, nameof(MarkTicketReceivedAndUpdateStatistics))));
	}

	private void MarkTicketReceivedAndUpdateStatistics() {
		_updateStatisticsTicketPending = false;
		UpdateStatistics();
	}

	private void UpdateStatistics() {
		_updateStatistics?.Invoke();
	}

	public void Handle(EventReaderSubscriptionMessage.ProgressChanged message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			var progressWorkItem = new ProgressWorkItem(CheckpointManager, message.Progress);
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

	private void Unsubscribed() {
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
				var checkpointSuggestedWorkItem = new CheckpointSuggestedWorkItem(this, message, CheckpointManager);
				_processingQueue.EnqueueTask(checkpointSuggestedWorkItem, checkpointTag, allowCurrentPosition: true);
			}

			ProcessEvent();
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(CoreProjectionManagementMessage.GetState message) {
		try {
			var getStateWorkItem = new GetStateWorkItem(_publisher, message.CorrelationId, message.ProjectionId, this, message.Partition);
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
		if (_subscriptionState is not PhaseSubscriptionState.Subscribing || message.SubscriptionId != _currentSubscriptionId)
			return;
		SubscriptionFailed("Reader subscription timed out");
	}

	public void Handle(EventReaderSubscriptionMessage.Failed message) => SubscriptionFailed(message.Reason);

	private void SubscriptionFailed(string reason) {
		if (_subscriptionState is PhaseSubscriptionState.Subscribed or PhaseSubscriptionState.Subscribing)
			_subscriptionDispatcher.Cancel(_currentSubscriptionId);
		_subscriptionState = PhaseSubscriptionState.Failed;
		_coreProjection.SetFaulted(reason);
	}

	private void UnsubscribeFromPreRecordedOrderEvents() {
		// projectionCorrelationId is used as a subscription identifier for delivery
		// of pre-recorded order events recovered by checkpoint manager
		_subscriptionDispatcher.Cancel(_projectionCorrelationId);
		_subscriptionState = PhaseSubscriptionState.Unsubscribed;
	}

	private void Subscribed(Guid subscriptionId) {
		_processingQueue.Subscribed(subscriptionId);
	}

	private ReaderSubscriptionOptions GetSubscriptionOptions() {
		return new(
			_projectionConfig.CheckpointUnhandledBytesThreshold, _projectionConfig.CheckpointHandledThreshold,
			_projectionConfig.CheckpointAfterMs, _stopOnEof, stopAfterNEvents: null, _enableContentTypeValidation);
	}

	private void SubscribeReaders(CheckpointTag checkpointTag) {
		_expectedSubscriptionMessageSequenceNumber = 0;
		_currentSubscriptionId = Guid.NewGuid();
		Subscribed(_currentSubscriptionId);
		var readerStrategy = _readerStrategy;
		if (readerStrategy != null) {
			_subscriptionState = PhaseSubscriptionState.Subscribing;
			_subscriptionDispatcher.PublishSubscribe(
				new(
					_currentSubscriptionId, checkpointTag, readerStrategy,
					GetSubscriptionOptions()), this, scheduleTimeout: true);
		} else {
			_coreProjection.Subscribed();
		}
	}

	private void SubscribeToPreRecordedOrderEvents() {
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
		Contract.Assert(CheckpointManager.LastProcessedEventPosition == from);
		if (fromCheckpoint) {
			SubscribeToPreRecordedOrderEvents();
			CheckpointManager.BeginLoadPrerecordedEvents(from);
		} else
			SubscribeReaders(from);
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
			if (emittedEvents is { Length: > 0 }) {
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

	private int GetBufferedEventCount() => _processingQueue.GetBufferedEventCount();

	private string GetStatus() => _processingQueue.GetStatus();

	protected EventProcessedResult InternalCommittedEventProcessed(
		string partition,
		EventReaderSubscriptionMessage.CommittedEventReceived message,
		EmittedEventEnvelope[] emittedEvents,
		PartitionState newPartitionState,
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
		               || (_isBiState && oldSharedState?.IsChanged(newSharedPartitionState) == true);

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
			var correlationId = message.Data.IsJson ? message.Data.Metadata.ParseCheckpointTagCorrelationId() : null;
			return new(
				partition, message.CheckpointTag, oldState, partitionState, oldSharedState, newSharedPartitionState,
				emittedEvents, message.Data.EventId, correlationId);
		}

		return null;
	}

	protected EventProcessedResult InternalPartitionDeletedProcessed(
		string partition,
		CheckpointTag deletePosition,
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
			partition: partition,
			checkpointTag: deletePosition,
			oldState: oldState,
			newState: partitionState,
			oldSharedState: oldSharedState,
			newSharedState: null,
			emittedEvents: null,
			causedBy: Guid.Empty,
			correlationId: null);
	}

	public void BeginGetPartitionStateAt(string statePartition, CheckpointTag at, Action<PartitionState> loadCompleted, bool lockLoaded) {
		if (statePartition == "") // root is always cached
		{
			// root partition is always locked
			var state = _partitionStateCache.TryGetAndLockPartitionState(statePartition, null);
			loadCompleted(state);
			return;
		}

		var s = lockLoaded
			? _partitionStateCache.TryGetAndLockPartitionState(statePartition, at)
			: _partitionStateCache.TryGetPartitionState(statePartition);
		if (s != null)
			loadCompleted(s);
		else {
			void Completed(PartitionState state) {
				if (lockLoaded)
					_partitionStateCache.CacheAndLockPartitionState(statePartition, state, at);
				else
					_partitionStateCache.CachePartitionState(statePartition, state);
				loadCompleted(state);
			}

			if (_projectionConfig.CheckpointsEnabled) {
				CheckpointManager.BeginLoadPartitionStateAt(statePartition, at, Completed);
			} else {
				var state = new PartitionState("", null, _zeroCheckpointTag);
				Completed(state);
			}
		}
	}

	public void FinalizeEventProcessing(EventProcessedResult result, CheckpointTag eventCheckpointTag, float progress) {
		if (_state != PhaseState.Running) return;

		//TODO: move to separate projection method and cache result in work item
		if (result != null) {
			_resultWriter.AccountPartition(result);
			if (_projectionConfig.EmitEventEnabled && result.EmittedEvents != null) {
				_resultWriter.EventsEmitted(
					result.EmittedEvents, result.CausedBy, result.CorrelationId);
				EmittedStreamsTracker.TrackEmittedStream(result.EmittedEvents.Select(x => x.Event).ToArray());
			}

			if (result.NewState != null) {
				_resultWriter.WriteRunningResult(result);
				CheckpointManager.StateUpdated(result.Partition, result.OldState, result.NewState);
			}

			if (result.NewSharedState != null) {
				CheckpointManager.StateUpdated("", result.OldSharedState, result.NewSharedState);
			}
		}

		CheckpointManager.EventProcessed(eventCheckpointTag, progress);
	}

	public void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action completed) {
		switch (_state) {
			case PhaseState.Running:
				CheckpointManager.RecordEventOrder(resolvedEvent, orderCheckpointTag, completed);
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
		info.Status += GetStatus();
		info.BufferedEvents += GetBufferedEventCount();
	}

	public CheckpointTag MakeZeroCheckpointTag() => _zeroCheckpointTag;

	public void EnsureUnsubscribed() {
		if (_subscriptionState is PhaseSubscriptionState.Subscribed) {
			Unsubscribed();
			// this way we distinguish pre-recorded events subscription
			if (_currentSubscriptionId != _projectionCorrelationId)
				_publisher.Publish(new ReaderSubscriptionManagement.Unsubscribe(_currentSubscriptionId));
		}
	}

	private readonly IEnvelope _inputQueueEnvelope;
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
