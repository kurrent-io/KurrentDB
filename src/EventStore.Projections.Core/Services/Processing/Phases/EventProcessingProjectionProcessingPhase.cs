// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.WorkItems;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public class EventProcessingProjectionProcessingPhase : EventSubscriptionBasedProjectionProcessingPhase,
	IHandle<EventReaderSubscriptionMessage.CommittedEventReceived>,
	IHandle<EventReaderSubscriptionMessage.PartitionEofReached>,
	IHandle<EventReaderSubscriptionMessage.PartitionDeleted>,
	IEventProcessingProjectionPhase {
	private readonly IProjectionStateHandler _projectionStateHandler;
	private readonly bool _definesStateTransform;
	private readonly StatePartitionSelector _statePartitionSelector;
	private readonly bool _isBiState;

	private string _handlerPartition;

	//private bool _sharedStateSet;
	private readonly Stopwatch _stopwatch;


	public EventProcessingProjectionProcessingPhase(
		CoreProjection coreProjection,
		Guid projectionCorrelationId,
		IPublisher publisher,
		IPublisher inputQueue,
		ProjectionConfig projectionConfig,
		Action updateStatistics,
		IProjectionStateHandler projectionStateHandler,
		PartitionStateCache partitionStateCache,
		bool definesStateTransform,
		string projectionName,
		ILogger logger,
		CheckpointTag zeroCheckpointTag,
		ICoreProjectionCheckpointManager coreProjectionCheckpointManager,
		StatePartitionSelector statePartitionSelector,
		ReaderSubscriptionDispatcher subscriptionDispatcher,
		IReaderStrategy readerStrategy,
		IResultWriter resultWriter,
		bool useCheckpoints,
		bool stopOnEof,
		bool isBiState,
		bool orderedPartitionProcessing,
		IEmittedStreamsTracker emittedStreamsTracker,
		bool enableContentTypeValidation)
		: base(
			publisher,
			inputQueue,
			coreProjection,
			projectionCorrelationId,
			coreProjectionCheckpointManager,
			projectionConfig,
			projectionName,
			logger,
			zeroCheckpointTag,
			partitionStateCache,
			resultWriter,
			updateStatistics,
			subscriptionDispatcher,
			readerStrategy,
			useCheckpoints,
			stopOnEof,
			orderedPartitionProcessing,
			isBiState,
			emittedStreamsTracker,
			enableContentTypeValidation) {
		_projectionStateHandler = projectionStateHandler;
		_definesStateTransform = definesStateTransform;
		_statePartitionSelector = statePartitionSelector;
		_isBiState = isBiState;

		_stopwatch = new Stopwatch();
	}

	public void Handle(EventReaderSubscriptionMessage.CommittedEventReceived message) {
		//TODO:  make sure this is no longer required : if (_state != State.StateLoaded)
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			CheckpointTag eventTag = message.CheckpointTag;
			var committedEventWorkItem = new CommittedEventWorkItem(this, message, _statePartitionSelector);
			_processingQueue.EnqueueTask(committedEventWorkItem, eventTag);
			if (_state == PhaseState.Running) // prevent processing mostly one projection
				EnsureTickPending();
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.PartitionDeleted message) {
		//TODO:  make sure this is no longer required : if (_state != State.StateLoaded)
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			if (_statePartitionSelector.EventReaderBasePartitionDeletedIsSupported()) {
				var partitionDeletedWorkItem = new PartitionDeletedWorkItem(this, message);
				_processingQueue.EnqueueOutOfOrderTask(partitionDeletedWorkItem);
				if (_state == PhaseState.Running) // prevent processing mostly one projection
					EnsureTickPending();
			}
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public void Handle(EventReaderSubscriptionMessage.PartitionEofReached message) {
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			var partitionCompletedWorkItem = new PartitionCompletedWorkItem(
				this, _checkpointManager, message.Partition, message.CheckpointTag);
			_processingQueue.EnqueueTask(
				partitionCompletedWorkItem, message.CheckpointTag, allowCurrentPosition: true);
			ProcessEvent();
		} catch (Exception ex) {
			_coreProjection.SetFaulted(ex);
		}
	}

	public EventProcessedResult ProcessCommittedEvent(
		EventReaderSubscriptionMessage.CommittedEventReceived message, string partition) {
		switch (_state) {
			case PhaseState.Running:
				var result = InternalProcessCommittedEvent(partition, message);
				return result;
			case PhaseState.Stopped:
				_logger.Error("Ignoring committed event in stopped state");
				return null;
			default:
				throw new NotSupportedException();
		}
	}

	public EventProcessedResult ProcessPartitionDeleted(string partition, CheckpointTag deletedPosition) {
		switch (_state) {
			case PhaseState.Running:
				var result = InternalProcessPartitionDeleted(partition, deletedPosition);
				return result;
			case PhaseState.Stopped:
				_logger.Error("Ignoring committed event in stopped state");
				return null;
			default:
				throw new NotSupportedException();
		}
	}

	private EventProcessedResult InternalProcessCommittedEvent(
		string partition, EventReaderSubscriptionMessage.CommittedEventReceived message) {
		string newState;
		string projectionResult;
		EmittedEventEnvelope[] emittedEvents;
		//TODO: support shared state
		string newSharedState;
		var hasBeenProcessed = SafeProcessEventByHandler(
			partition, message, out newState, out newSharedState, out projectionResult, out emittedEvents);
		if (hasBeenProcessed) {
			var newPartitionState = new PartitionState(newState, projectionResult, message.CheckpointTag);
			var newSharedPartitionState = newSharedState != null
				? new PartitionState(newSharedState, null, message.CheckpointTag)
				: null;

			return InternalCommittedEventProcessed(
				partition, message, emittedEvents, newPartitionState, newSharedPartitionState);
		}

		return null;
	}

	private EventProcessedResult InternalProcessPartitionDeleted(
		string partition, CheckpointTag deletedPosition) {
		string newState;
		string projectionResult;
		var hasBeenProcessed = SafeProcessPartitionDeletedByHandler(
			partition, deletedPosition, out newState, out projectionResult);
		if (hasBeenProcessed) {
			var newPartitionState = new PartitionState(newState, projectionResult, deletedPosition);

			return InternalPartitionDeletedProcessed(partition, deletedPosition, newPartitionState);
		}

		return null;
	}

	private bool SafeProcessEventByHandler(
		string partition, EventReaderSubscriptionMessage.CommittedEventReceived message, out string newState,
		out string newSharedState, out string projectionResult, out EmittedEventEnvelope[] emittedEvents) {
		projectionResult = null;
		//TODO: not emitting (optimized) projection handlers can skip serializing state on each processed event
		bool hasBeenProcessed;
		try {
			hasBeenProcessed = ProcessEventByHandler(
				partition, message, out newState, out newSharedState, out projectionResult, out emittedEvents);
		} catch (Exception ex) {
			// update progress to reflect exact fault position
			_checkpointManager.Progress(message.Progress);
			SetFaulting(
				String.Format(
					"The {0} projection failed to process an event.\r\nHandler: {1}\r\nEvent Position: {2}\r\n\r\nMessage:\r\n\r\n{3}",
					_projectionName, GetHandlerTypeName(), message.CheckpointTag, ex.Message), ex);
			newState = null;
			newSharedState = null;
			emittedEvents = null;
			hasBeenProcessed = false;
		}

		newState = newState ?? "";
		return hasBeenProcessed;
	}

	private bool SafeProcessPartitionDeletedByHandler(
		string partition, CheckpointTag deletedPosition, out string newState,
		out string projectionResult) {
		projectionResult = null;
		//TODO: not emitting (optimized) projection handlers can skip serializing state on each processed event
		bool hasBeenProcessed;
		try {
			hasBeenProcessed = ProcessPartitionDeletedByHandler(
				partition, deletedPosition, out newState, out projectionResult);
		} catch (Exception ex) {
			SetFaulting(
				String.Format(
					"The {0} projection failed to process a delete partition notification.\r\nHandler: {1}\r\nEvent Position: {2}\r\n\r\nMessage:\r\n\r\n{3}",
					_projectionName, GetHandlerTypeName(), deletedPosition, ex.Message), ex);
			newState = null;
			hasBeenProcessed = false;
		}

		newState = newState ?? "";
		return hasBeenProcessed;
	}

	private string GetHandlerTypeName() {
		return _projectionStateHandler.GetType().Namespace + "." + _projectionStateHandler.GetType().Name;
	}

	private bool ProcessEventByHandler(
		string partition, EventReaderSubscriptionMessage.CommittedEventReceived message, out string newState,
		out string newSharedState, out string projectionResult, out EmittedEventEnvelope[] emittedEvents) {
		projectionResult = null;
		var newPatitionInitialized = InitOrLoadHandlerState(partition);
		_stopwatch.Start();
		EmittedEventEnvelope[] eventsEmittedOnInitialization = null;
		if (newPatitionInitialized) {
			_projectionStateHandler.ProcessPartitionCreated(
				partition, message.CheckpointTag, message.Data, out eventsEmittedOnInitialization);
		}

		var result = _projectionStateHandler.ProcessEvent(
			partition, message.CheckpointTag, message.EventCategory, message.Data, out newState, out newSharedState,
			out emittedEvents);
		if (result) {
			var oldState = _partitionStateCache.GetLockedPartitionState(partition);
			//TODO: depending on query processing final state to result transformation should happen either here (if EOF) on while writing results
			if ( /*_producesRunningResults && */oldState.State != newState) {
				if (_definesStateTransform) {
					projectionResult = _projectionStateHandler.TransformStateToResult();
				} else {
					projectionResult = newState;
				}
			} else {
				projectionResult = oldState.Result;
			}
		}

		_stopwatch.Stop();
		if (eventsEmittedOnInitialization != null) {
			if (emittedEvents == null || emittedEvents.Length == 0)
				emittedEvents = eventsEmittedOnInitialization;
			else
				emittedEvents = eventsEmittedOnInitialization.Concat(emittedEvents).ToArray();
		}

		return result;
	}

	private bool ProcessPartitionDeletedByHandler(
		string partition, CheckpointTag deletePosition, out string newState,
		out string projectionResult) {
		projectionResult = null;
		InitOrLoadHandlerState(partition);
		_stopwatch.Start();
		var result = _projectionStateHandler.ProcessPartitionDeleted(
			partition, deletePosition, out newState);
		if (result) {
			var oldState = _partitionStateCache.GetLockedPartitionState(partition);
			//TODO: depending on query processing final state to result transformation should happen either here (if EOF) on while writing results
			if ( /*_producesRunningResults && */oldState.State != newState) {
				if (_definesStateTransform) {
					projectionResult = _projectionStateHandler.TransformStateToResult();
				} else {
					projectionResult = newState;
				}
			} else {
				projectionResult = oldState.Result;
			}
		}

		_stopwatch.Stop();
		return result;
	}

	/// <summary>
	/// initializes or loads existing partition state
	/// </summary>
	/// <param name="partition"></param>
	/// <returns>true - if new partition state was initialized</returns>
	private bool InitOrLoadHandlerState(string partition) {
		if (_handlerPartition == partition)
			return false;

		var newState = _partitionStateCache.GetLockedPartitionState(partition);
		_handlerPartition = partition;
		var initialized = false;
		if (newState != null && !String.IsNullOrEmpty(newState.State))
			_projectionStateHandler.Load(newState.State);
		else {
			initialized = true;
			_projectionStateHandler.Initialize();
		}

		//if (!_sharedStateSet && _isBiState)
		if (_isBiState) {
			var newSharedState = _partitionStateCache.GetLockedPartitionState("");
			if (newSharedState != null && !String.IsNullOrEmpty(newSharedState.State))
				_projectionStateHandler.LoadShared(newSharedState.State);
			else
				_projectionStateHandler.InitializeShared();
		}

		return initialized;
	}

	public override void NewCheckpointStarted(CheckpointTag at) {
		if (!(_state == PhaseState.Running || _state == PhaseState.Starting)) {
			_logger.Debug("Starting a checkpoint in non-runnable state");
			return;
		}

		var checkpointHandler = _projectionStateHandler as IProjectionCheckpointHandler;
		if (checkpointHandler != null) {
			EmittedEventEnvelope[] emittedEvents;
			try {
				checkpointHandler.ProcessNewCheckpoint(at, out emittedEvents);
			} catch (Exception ex) {
				var faultedReason =
					String.Format(
						"The {0} projection failed to process a checkpoint start.\r\nHandler: {1}\r\nEvent Position: {2}\r\n\r\nMessage:\r\n\r\n{3}",
						_projectionName, GetHandlerTypeName(), at, ex.Message);
				SetFaulting(faultedReason, ex);
				emittedEvents = null;
			}

			if (emittedEvents != null && emittedEvents.Length > 0) {
				if (!ValidateEmittedEvents(emittedEvents))
					return;

				if (_state == PhaseState.Running || _state == PhaseState.Starting)
					_resultWriter.EventsEmitted(
						emittedEvents, Guid.Empty, correlationId: null);
			}
		}
	}

	public override void GetStatistics(ProjectionStatistics info) {
		base.GetStatistics(info);
		info.CoreProcessingTime = _stopwatch.ElapsedMilliseconds;
	}

	public override void Dispose() {
		if (_projectionStateHandler != null)
			_projectionStateHandler.Dispose();
	}
}
