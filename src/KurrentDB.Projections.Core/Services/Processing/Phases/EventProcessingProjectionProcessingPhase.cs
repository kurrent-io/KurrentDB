// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.WorkItems;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Phases;

public class EventProcessingProjectionProcessingPhase
	: EventSubscriptionBasedProjectionProcessingPhase,
		IHandle<EventReaderSubscriptionMessage.CommittedEventReceived>,
		IHandle<EventReaderSubscriptionMessage.PartitionDeleted>,
		IEventProcessingProjectionPhase {
	private readonly IProjectionStateHandler _projectionStateHandler;
	private readonly bool _definesStateTransform;
	private readonly StatePartitionSelector _statePartitionSelector;
	private readonly bool _isBiState;

	private string _handlerPartition;

	private readonly Stopwatch _stopwatch = new();

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
	}

	public void Handle(EventReaderSubscriptionMessage.CommittedEventReceived message) {
		//TODO:  make sure this is no longer required : if (_state != State.StateLoaded)
		if (IsOutOfOrderSubscriptionMessage(message))
			return;
		RegisterSubscriptionMessage(message);
		try {
			CheckpointTag eventTag = message.CheckpointTag;
			var committedEventWorkItem = new CommittedEventWorkItem(this, message, _statePartitionSelector);
			ProcessingQueue.EnqueueTask(committedEventWorkItem, eventTag, false);
			if (State == PhaseState.Running) // prevent processing mostly one projection
				EnsureTickPending();
		} catch (Exception ex) {
			CoreProjection.SetFaulted(ex);
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
				ProcessingQueue.EnqueueOutOfOrderTask(partitionDeletedWorkItem);
				if (State == PhaseState.Running) // prevent processing mostly one projection
					EnsureTickPending();
			}
		} catch (Exception ex) {
			CoreProjection.SetFaulted(ex);
		}
	}

	public EventProcessedResult ProcessCommittedEvent(
		EventReaderSubscriptionMessage.CommittedEventReceived message,
		string partition) {
		switch (State) {
			case PhaseState.Running:
				var result = InternalProcessCommittedEvent(partition, message);
				return result;
			case PhaseState.Stopped:
				Logger.Error("Ignoring committed event in stopped state");
				return null;
			default:
				throw new NotSupportedException();
		}
	}

	public EventProcessedResult ProcessPartitionDeleted(string partition, CheckpointTag deletedPosition) {
		switch (State) {
			case PhaseState.Running:
				var result = InternalProcessPartitionDeleted(partition, deletedPosition);
				return result;
			case PhaseState.Stopped:
				Logger.Error("Ignoring committed event in stopped state");
				return null;
			default:
				throw new NotSupportedException();
		}
	}

	private EventProcessedResult InternalProcessCommittedEvent(string partition,
		EventReaderSubscriptionMessage.CommittedEventReceived message) {
		//TODO: support shared state
		var hasBeenProcessed = SafeProcessEventByHandler(
			partition, message, out var newState, out var newSharedState, out var projectionResult, out var emittedEvents);
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

	private EventProcessedResult InternalProcessPartitionDeleted(string partition, CheckpointTag deletedPosition) {
		var hasBeenProcessed = SafeProcessPartitionDeletedByHandler(
			partition, deletedPosition, out var newState, out var projectionResult);
		if (hasBeenProcessed) {
			var newPartitionState = new PartitionState(newState, projectionResult, deletedPosition);

			return InternalPartitionDeletedProcessed(partition, deletedPosition, newPartitionState);
		}

		return null;
	}

	private bool SafeProcessEventByHandler(
		string partition,
		EventReaderSubscriptionMessage.CommittedEventReceived message,
		out string newState,
		out string newSharedState,
		out string projectionResult,
		out EmittedEventEnvelope[] emittedEvents) {
		projectionResult = null;
		//TODO: not emitting (optimized) projection handlers can skip serializing state on each processed event
		bool hasBeenProcessed;
		try {
			hasBeenProcessed = ProcessEventByHandler(partition, message, out newState, out newSharedState, out projectionResult,
				out emittedEvents);
		} catch (Exception ex) {
			// update progress to reflect exact fault position
			CheckpointManager.Progress(message.Progress);
			SetFaulting(
				$"The {ProjectionName} projection failed to process an event.\r\nHandler: {GetHandlerTypeName()}\r\nEvent Position: {message.CheckpointTag}\r\n\r\nMessage:\r\n\r\n{ex.Message}",
				ex);
			newState = null;
			newSharedState = null;
			emittedEvents = null;
			hasBeenProcessed = false;
		}

		newState ??= "";
		return hasBeenProcessed;
	}

	private bool SafeProcessPartitionDeletedByHandler(
		string partition,
		CheckpointTag deletedPosition,
		out string newState,
		out string projectionResult) {
		projectionResult = null;
		//TODO: not emitting (optimized) projection handlers can skip serializing state on each processed event
		bool hasBeenProcessed;
		try {
			hasBeenProcessed = ProcessPartitionDeletedByHandler(
				partition, deletedPosition, out newState, out projectionResult);
		} catch (Exception ex) {
			SetFaulting(
				$"The {ProjectionName} projection failed to process a delete partition notification.\r\nHandler: {GetHandlerTypeName()}\r\nEvent Position: {deletedPosition}\r\n\r\nMessage:\r\n\r\n{ex.Message}",
				ex);
			newState = null;
			hasBeenProcessed = false;
		}

		newState ??= "";
		return hasBeenProcessed;
	}

	private string GetHandlerTypeName() => $"{_projectionStateHandler.GetType().Namespace}.{_projectionStateHandler.GetType().Name}";

	private bool ProcessEventByHandler(
		string partition,
		EventReaderSubscriptionMessage.CommittedEventReceived message,
		out string newState,
		out string newSharedState,
		out string projectionResult,
		out EmittedEventEnvelope[] emittedEvents) {
		projectionResult = null;
		var isPartitionInitialized = InitOrLoadHandlerState(partition);
		_stopwatch.Start();
		EmittedEventEnvelope[] eventsEmittedOnInitialization = null;
		if (isPartitionInitialized) {
			_projectionStateHandler.ProcessPartitionCreated(partition, message.CheckpointTag, message.Data, out eventsEmittedOnInitialization);
		}

		var result = _projectionStateHandler.ProcessEvent(
			partition, message.CheckpointTag, message.EventCategory, message.Data, out newState, out newSharedState,
			out emittedEvents);
		if (result) {
			var oldState = PartitionStateCache.GetLockedPartitionState(partition);
			//TODO: depending on query processing final state to result transformation should happen either here (if EOF) on while writing results
			projectionResult = oldState.State != newState
				? _definesStateTransform ? _projectionStateHandler.TransformStateToResult() : newState
				: oldState.Result;
		}

		_stopwatch.Stop();
		if (eventsEmittedOnInitialization != null) {
			emittedEvents = emittedEvents == null || emittedEvents.Length == 0
				? eventsEmittedOnInitialization
				: eventsEmittedOnInitialization.Concat(emittedEvents).ToArray();
		}

		return result;
	}

	private bool ProcessPartitionDeletedByHandler(
		string partition,
		CheckpointTag deletePosition,
		out string newState,
		out string projectionResult) {
		projectionResult = null;
		InitOrLoadHandlerState(partition);
		_stopwatch.Start();
		var result = _projectionStateHandler.ProcessPartitionDeleted(partition, deletePosition, out newState);
		if (result) {
			var oldState = PartitionStateCache.GetLockedPartitionState(partition);
			//TODO: depending on query processing final state to result transformation should happen either here (if EOF) on while writing results
			projectionResult = oldState.State != newState
				? _definesStateTransform ? _projectionStateHandler.TransformStateToResult() : newState
				: oldState.Result;
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

		var newState = PartitionStateCache.GetLockedPartitionState(partition);
		_handlerPartition = partition;
		var initialized = false;
		if (newState != null && !string.IsNullOrEmpty(newState.State))
			_projectionStateHandler.Load(newState.State);
		else {
			initialized = true;
			_projectionStateHandler.Initialize();
		}

		//if (!_sharedStateSet && _isBiState)
		if (_isBiState) {
			var newSharedState = PartitionStateCache.GetLockedPartitionState("");
			if (newSharedState != null && !string.IsNullOrEmpty(newSharedState.State))
				_projectionStateHandler.LoadShared(newSharedState.State);
			else
				_projectionStateHandler.InitializeShared();
		}

		return initialized;
	}

	public override void NewCheckpointStarted(CheckpointTag at) {
		if (State is not (PhaseState.Running or PhaseState.Starting)) {
			Logger.Debug("Starting a checkpoint in non-runnable state");
			return;
		}

		if (_projectionStateHandler is IProjectionCheckpointHandler checkpointHandler) {
			EmittedEventEnvelope[] emittedEvents;
			try {
				checkpointHandler.ProcessNewCheckpoint(at, out emittedEvents);
			} catch (Exception ex) {
				var faultedReason =
					$"The {ProjectionName} projection failed to process a checkpoint start.\r\nHandler: {GetHandlerTypeName()}\r\nEvent Position: {at}\r\n\r\nMessage:\r\n\r\n{ex.Message}";
				SetFaulting(faultedReason, ex);
				emittedEvents = null;
			}

			if (emittedEvents is { Length: > 0 }) {
				if (!ValidateEmittedEvents(emittedEvents))
					return;

				if (State is PhaseState.Running or PhaseState.Starting)
					ResultWriter.EventsEmitted(emittedEvents, Guid.Empty, correlationId: null);
			}
		}
	}

	public override void GetStatistics(ProjectionStatistics info) {
		base.GetStatistics(info);
		info.CoreProcessingTime = _stopwatch.ElapsedMilliseconds;
	}

	public override void Dispose() {
		_projectionStateHandler?.Dispose();
	}
}
