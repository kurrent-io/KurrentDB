// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
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

public abstract partial class EventSubscriptionBasedProjectionProcessingPhase
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
	protected readonly ICoreProjectionForProcessingPhase CoreProjection;
	protected readonly string ProjectionName;
	protected readonly ILogger Logger;
	protected readonly CoreProjectionQueue ProcessingQueue;
	protected readonly PartitionStateCache PartitionStateCache;
	protected readonly IResultWriter ResultWriter;
	protected PhaseState State;
	private readonly IPublisher _publisher;
	private readonly Guid _projectionCorrelationId;
	private readonly IProgressResultWriter _progressResultWriter;
	private readonly ProjectionConfig _projectionConfig;
	private readonly CheckpointTag _zeroCheckpointTag;
	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;
	private readonly IReaderStrategy _readerStrategy;
	private readonly bool _useCheckpoints;
	private long _expectedSubscriptionMessageSequenceNumber = -1;
	private Guid _currentSubscriptionId;
	private PhaseSubscriptionState _subscriptionState;
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
		CoreProjection = coreProjection;
		_projectionCorrelationId = projectionCorrelationId;
		CheckpointManager = checkpointManager;
		_projectionConfig = projectionConfig;
		ProjectionName = projectionName;
		Logger = logger;
		_zeroCheckpointTag = zeroCheckpointTag;
		PartitionStateCache = partitionStateCache;
		ResultWriter = resultWriter;
		_updateStatistics = updateStatistics;
		ProcessingQueue = new(publisher, projectionConfig.PendingEventsThreshold, orderedPartitionProcessing);
		ProcessingQueue.EnsureTickPending += EnsureTickPending;
		_subscriptionDispatcher = subscriptionDispatcher;
		_readerStrategy = readerStrategy;
		_useCheckpoints = useCheckpoints;
		_stopOnEof = stopOnEof;
		_isBiState = isBiState;
		_progressResultWriter = new ProgressResultWriter(ResultWriter);
		_inOutQueueEnvelope = inputQueue;
		EmittedStreamsTracker = emittedStreamsTracker;
		_enableContentTypeValidation = enableContentTypeValidation;
	}

	public CheckpointTag LastProcessedEventPosition => CoreProjection.LastProcessedEventPosition;

	public ICoreProjectionCheckpointManager CheckpointManager { get; }

	public IEmittedStreamsTracker EmittedStreamsTracker { get; }

	protected bool IsOutOfOrderSubscriptionMessage(EventReaderSubscriptionMessageBase message) {
		if (_currentSubscriptionId != message.SubscriptionId)
			return true;
		return _expectedSubscriptionMessageSequenceNumber == message.SubscriptionMessageSequenceNumber
			? false
			: throw new InvalidOperationException("Out of order message detected");
	}

	protected void RegisterSubscriptionMessage(EventReaderSubscriptionMessageBase message) {
		_expectedSubscriptionMessageSequenceNumber = message.SubscriptionMessageSequenceNumber + 1;
	}

	protected void EnsureTickPending() {
		CoreProjection.EnsureTickPending();
	}

	public void ProcessEvent() {
		ProcessingQueue.ProcessEvent();
		EnsureUpdateStatisticsTickPending();
	}

	private void EnsureUpdateStatisticsTickPending() {
		if (_updateStatisticsTicketPending)
			return;
		_updateStatisticsTicketPending = true;
		_publisher.Publish(
			TimerMessage.Schedule.Create(
				_updateInterval,
				_inOutQueueEnvelope,
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
			var progressWorkItem = new ProgressWorkItem(CheckpointManager, _progressResultWriter, message.Progress);
			ProcessingQueue.EnqueueTask(progressWorkItem, message.CheckpointTag, allowCurrentPosition: true);
			ProcessEvent();
		} catch (Exception ex) {
			CoreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.SubscriptionStarted message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
		} catch (Exception ex) {
			CoreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.NotAuthorized message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			var progressWorkItem = new NotAuthorizedWorkItem();
			ProcessingQueue.EnqueueTask(progressWorkItem, message.CheckpointTag, allowCurrentPosition: true);
			ProcessEvent();
		} catch (Exception ex) {
			CoreProjection.SetFaulted(ex);
		}
	}

	private void Unsubscribed() {
		_subscriptionDispatcher.Cancel(_projectionCorrelationId);
		_subscriptionState = PhaseSubscriptionState.Unsubscribed;
		ProcessingQueue.Unsubscribed();
	}

	public void Handle(EventReaderSubscriptionMessage.EofReached message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			Unsubscribed();
			var completedWorkItem = new CompletedWorkItem(this);
			ProcessingQueue.EnqueueTask(completedWorkItem, message.CheckpointTag, allowCurrentPosition: true);
			ProcessEvent();
		} catch (Exception ex) {
			CoreProjection.SetFaulted(ex);
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
				ProcessingQueue.EnqueueTask(checkpointSuggestedWorkItem, checkpointTag, allowCurrentPosition: true);
			}

			ProcessEvent();
		} catch (Exception ex) {
			CoreProjection.SetFaulted(ex);
		}
	}

	public void Handle(CoreProjectionManagementMessage.GetState message) {
		try {
			var getStateWorkItem = new GetStateWorkItem(_publisher, message.CorrelationId, message.ProjectionId, this, message.Partition);
			ProcessingQueue.EnqueueOutOfOrderTask(getStateWorkItem);
			ProcessEvent();
		} catch (Exception ex) {
			_publisher.Publish(
				new CoreProjectionStatusMessage.StateReport(
					message.CorrelationId, _projectionCorrelationId, message.Partition, state: null,
					position: null));
			CoreProjection.SetFaulted(ex);
		}
	}

	public void Handle(CoreProjectionManagementMessage.GetResult message) {
		try {
			var getResultWorkItem = new GetResultWorkItem(
				_publisher, message.CorrelationId, message.ProjectionId, this, message.Partition);
			ProcessingQueue.EnqueueOutOfOrderTask(getResultWorkItem);
			ProcessEvent();
		} catch (Exception ex) {
			_publisher.Publish(
				new CoreProjectionStatusMessage.ResultReport(
					message.CorrelationId, _projectionCorrelationId, message.Partition, result: null,
					position: null));
			CoreProjection.SetFaulted(ex);
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
		CoreProjection.SetFaulted(reason);
	}

	private void UnsubscribeFromPreRecordedOrderEvents() {
		// projectionCorrelationId is used as a subscription identifier for delivery
		// of pre-recorded order events recovered by checkpoint manager
		_subscriptionDispatcher.Cancel(_projectionCorrelationId);
		_subscriptionState = PhaseSubscriptionState.Unsubscribed;
	}

	private void Subscribed(Guid subscriptionId) {
		ProcessingQueue.Subscribed(subscriptionId);
	}

	private ReaderSubscriptionOptions GetSubscriptionOptions()
		=> new(
			_projectionConfig.CheckpointUnhandledBytesThreshold, _projectionConfig.CheckpointHandledThreshold,
			_projectionConfig.CheckpointAfterMs,
			_stopOnEof, stopAfterNEvents: null, _enableContentTypeValidation);

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
			CoreProjection.Subscribed();
		}
	}

	private void SubscribeToPreRecordedOrderEvents() {
		var coreProjection = (CoreProjection)CoreProjection;
		// projectionCorrelationId is used as a subscription identifier for delivery
		// of pre-recorded order events recovered by checkpoint manager
		_expectedSubscriptionMessageSequenceNumber = 0;
		_currentSubscriptionId = coreProjection._projectionCorrelationId;
		_subscriptionDispatcher.Subscribed(coreProjection._projectionCorrelationId, this);
		// even if it is not a real subscription we need to unsubscribe
		_subscriptionState = PhaseSubscriptionState.Subscribed;
	}

	public virtual void Subscribe(CheckpointTag from, bool fromCheckpoint) {
		Debug.Assert(CheckpointManager.LastProcessedEventPosition == from);
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

	public CheckpointTag AdjustTag(CheckpointTag tag) => _readerStrategy.PositionTagger.AdjustTag(tag);

	protected void SetFaulting(string faultedReason, Exception ex = null) {
		if (Logger != null) {
			if (ex != null)
				Logger.Error(ex, faultedReason);
			else
				Logger.Error(faultedReason);
		}

		CoreProjection.SetFaulting(faultedReason);
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
		ProcessingQueue.InitializeQueue(adjustedCheckpointTag);
	}

	private int GetBufferedEventCount() => ProcessingQueue.GetBufferedEventCount();

	private string GetStatus() => ProcessingQueue.GetStatus();

	protected EventProcessedResult InternalCommittedEventProcessed(
		string partition,
		EventReaderSubscriptionMessage.CommittedEventReceived message,
		EmittedEventEnvelope[] emittedEvents,
		PartitionState newPartitionState,
		PartitionState newSharedPartitionState) {
		if (_subscriptionState != PhaseSubscriptionState.Subscribed)
			Logger?.Verbose("Got CommittedEventReceived in {state} SubscriptionState, but expected to be in {expectedState}",
				_subscriptionState, PhaseSubscriptionState.Subscribed);

		if (!ValidateEmittedEvents(emittedEvents))
			return null;

		bool eventsWereEmitted = emittedEvents != null;
		var oldState = PartitionStateCache.GetLockedPartitionState(partition);
		var oldSharedState = _isBiState ? PartitionStateCache.GetLockedPartitionState("") : null;
		bool changed = oldState.IsChanged(newPartitionState)
		               || (_isBiState && oldSharedState.IsChanged(newSharedPartitionState));

		PartitionState partitionState = null;
		// NOTE: projectionResult cannot change independently unless projection definition has changed
		if (changed) {
			var lockPartitionStateAt = partition != "" ? message.CheckpointTag : null;
			partitionState = newPartitionState;
			PartitionStateCache.CacheAndLockPartitionState(partition, partitionState, lockPartitionStateAt);
			if (_isBiState) {
				PartitionStateCache.CacheAndLockPartitionState("", newSharedPartitionState, null);
			}
		}

		if (changed || eventsWereEmitted) {
			var correlationId = message.Data.IsJson ? message.Data.Metadata.ParseCheckpointTagCorrelationId() : null;
			return new EventProcessedResult(
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
		var oldState = PartitionStateCache.GetLockedPartitionState(partition);
		var oldSharedState = _isBiState ? PartitionStateCache.GetLockedPartitionState("") : null;
		bool changed = oldState.IsChanged(newPartitionState);

		PartitionState partitionState = null;
		// NOTE: projectionResult cannot change independently unless projection definition has changed
		if (changed) {
			var lockPartitionStateAt = partition != "" ? deletePosition : null;
			partitionState = newPartitionState;
			PartitionStateCache.CacheAndLockPartitionState(partition, partitionState, lockPartitionStateAt);
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

	public void BeginGetPartitionStateAt(
		string statePartition,
		CheckpointTag at,
		Action<PartitionState> loadCompleted,
		bool lockLoaded) {
		if (statePartition == "") // root is always cached
		{
			// root partition is always locked
			var state = PartitionStateCache.TryGetAndLockPartitionState(statePartition, null);
			loadCompleted(state);
		} else {
			var s = lockLoaded
				? PartitionStateCache.TryGetAndLockPartitionState(statePartition, at)
				: PartitionStateCache.TryGetPartitionState(statePartition);
			if (s != null)
				loadCompleted(s);
			else {
				void Completed(PartitionState state) {
					if (lockLoaded)
						PartitionStateCache.CacheAndLockPartitionState(statePartition, state, at);
					else
						PartitionStateCache.CachePartitionState(statePartition, state);
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
	}

	public void FinalizeEventProcessing(EventProcessedResult result, CheckpointTag eventCheckpointTag, float progress) {
		if (State != PhaseState.Running) return;

		//TODO: move to separate projection method and cache result in work item
		if (result != null) {
			ResultWriter.AccountPartition(result);
			if (_projectionConfig.EmitEventEnabled && result.EmittedEvents != null) {
				ResultWriter.EventsEmitted(
					result.EmittedEvents, result.CausedBy, result.CorrelationId);
				EmittedStreamsTracker.TrackEmittedStream(result.EmittedEvents.Select(x => x.Event).ToArray());
			}

			if (result.NewState != null) {
				ResultWriter.WriteRunningResult(result);
				CheckpointManager.StateUpdated(result.Partition, result.OldState, result.NewState);
			}

			if (result.NewSharedState != null) {
				CheckpointManager.StateUpdated("", result.OldSharedState, result.NewSharedState);
			}
		}

		CheckpointManager.EventProcessed(eventCheckpointTag, progress);
		_progressResultWriter.WriteProgress(progress);
	}

	public void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action completed) {
		switch (State) {
			case PhaseState.Running:
				CheckpointManager.RecordEventOrder(resolvedEvent, orderCheckpointTag, completed);
				break;
			case PhaseState.Stopped:
				Logger.Error("Should not receive events in stopped state anymore");
				completed(); // allow collecting events for debugging
				break;
		}
	}

	public void Complete() {
		//NOTE: no need for EnsureUnsubscribed  as EOF
		Unsubscribed();
		CoreProjection.CompletePhase();
	}

	public void SetCurrentCheckpointSuggestedWorkItem(CheckpointSuggestedWorkItem checkpointSuggestedWorkItem) {
		CoreProjection.SetCurrentCheckpointSuggestedWorkItem(checkpointSuggestedWorkItem);
	}

	public virtual void GetStatistics(ProjectionStatistics info) {
		info.Status += GetStatus();
		info.BufferedEvents += GetBufferedEventCount();
	}

	public CheckpointTag MakeZeroCheckpointTag() => _zeroCheckpointTag;

	public void EnsureUnsubscribed() {
		if (_subscriptionState is not PhaseSubscriptionState.Subscribed) return;

		Unsubscribed();
		// this way we distinguish pre-recorded events subscription
		if (_currentSubscriptionId != _projectionCorrelationId)
			_publisher.Publish(new ReaderSubscriptionManagement.Unsubscribe(_currentSubscriptionId));
	}

	private readonly IEnvelope _inOutQueueEnvelope;
	private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(250);
	private bool _updateStatisticsTicketPending;

	public void Handle(EventReaderSubscriptionMessage.ReaderAssignedReader message) {
		if (State != PhaseState.Starting)
			return;
		if (_subscriptionState is not PhaseSubscriptionState.Subscribing || message.SubscriptionId != _currentSubscriptionId)
			return;
		_subscriptionState = PhaseSubscriptionState.Subscribed;
		CoreProjection.Subscribed();
	}

	public abstract void Dispose();

	public void SetProjectionState(PhaseState state) {
		var starting = State == PhaseState.Starting && state == PhaseState.Running;

		State = state;
		ProcessingQueue.SetIsRunning(state == PhaseState.Running);
		if (starting)
			NewCheckpointStarted(LastProcessedEventPosition);
	}
}
