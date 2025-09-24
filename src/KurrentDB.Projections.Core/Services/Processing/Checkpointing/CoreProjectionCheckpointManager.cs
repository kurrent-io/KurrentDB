// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public abstract class CoreProjectionCheckpointManager
	: IProjectionCheckpointManager,
		ICoreProjectionCheckpointManager,
		IEmittedEventWriter {
	protected readonly ProjectionNamesBuilder NamingBuilder;
	protected readonly ProjectionConfig ProjectionConfig;
	protected readonly ILogger Logger;
	protected readonly IPublisher Publisher;
	private readonly int _maxProjectionStateSize;
	private readonly int _maxProjectionStateSizeThreshold;
	private readonly bool _usePersistentCheckpoints;

	private readonly Guid _projectionCorrelationId;
	private readonly CheckpointTag _zeroTag;

	protected ProjectionCheckpoint CurrentCheckpoint;
	private ProjectionCheckpoint _closingCheckpoint;
	internal CheckpointTag _requestedCheckpointPosition;
	private bool _inCheckpoint;
	private PartitionState _requestedCheckpointState;
	private CheckpointTag _lastCompletedCheckpointPosition;
	private readonly PositionTracker _lastProcessedEventPosition;
	private float _lastProcessedEventProgress;

	private int _eventsProcessedAfterRestart;
	private bool _started;
	protected bool _stopping;
	protected bool _stopped;

	private PartitionState _currentProjectionState;
	private readonly ConcurrentDictionary<string, int> _stateSizeByPartition = new();

	protected CoreProjectionCheckpointManager(
		IPublisher publisher,
		Guid projectionCorrelationId,
		ProjectionConfig projectionConfig,
		string name,
		PositionTagger positionTagger,
		ProjectionNamesBuilder namingBuilder,
		bool usePersistentCheckpoints,
		int maxProjectionStateSize) {
		ArgumentNullException.ThrowIfNull(publisher);
		ArgumentNullException.ThrowIfNull(projectionConfig);
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(positionTagger);
		ArgumentNullException.ThrowIfNull(namingBuilder);
		if (name == "")
			throw new ArgumentException(null, nameof(name));
		if (maxProjectionStateSize <= 0)
			throw new ArgumentException(null, nameof(maxProjectionStateSize));

		_lastProcessedEventPosition = new(positionTagger);
		_zeroTag = positionTagger.MakeZeroCheckpointTag();

		Publisher = publisher;
		_projectionCorrelationId = projectionCorrelationId;
		ProjectionConfig = projectionConfig;
		Logger = Log.ForContext<CoreProjectionCheckpointManager>();
		NamingBuilder = namingBuilder;
		_usePersistentCheckpoints = usePersistentCheckpoints;
		_requestedCheckpointState = new("", null, _zeroTag);
		_currentProjectionState = new("", null, _zeroTag);
		_maxProjectionStateSize = maxProjectionStateSize;
		_maxProjectionStateSizeThreshold = (int)(0.50 * _maxProjectionStateSize);
	}

	protected abstract ProjectionCheckpoint CreateProjectionCheckpoint(CheckpointTag checkpointPosition);

	protected abstract void BeginWriteCheckpoint(CheckpointTag requestedCheckpointPosition, string requestedCheckpointState);

	protected abstract void CapturePartitionStateUpdated(string partition, PartitionState oldState, PartitionState newState);

	protected abstract void EmitPartitionCheckpoints();

	public abstract void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action committed);

	public abstract void BeginLoadPartitionStateAt(string statePartition,
		CheckpointTag requestedStateCheckpointTag,
		Action<PartitionState> loadCompleted);

	public virtual void Initialize() {
		CurrentCheckpoint?.Dispose();
		_closingCheckpoint?.Dispose();
		CurrentCheckpoint = null;
		_closingCheckpoint = null;
		_requestedCheckpointPosition = null;
		_inCheckpoint = false;
		_requestedCheckpointState = new("", null, _zeroTag);
		_lastCompletedCheckpointPosition = null;
		_lastProcessedEventPosition.Initialize();
		_lastProcessedEventProgress = -1;
		_eventsProcessedAfterRestart = 0;
		_started = false;
		_stopping = false;
		_stopped = false;
		_currentProjectionState = new("", null, _zeroTag);
	}

	public virtual void Start(CheckpointTag checkpointTag, PartitionState rootPartitionState) {
		if (_started)
			throw new InvalidOperationException("Already started");
		_started = true;

		if (rootPartitionState != null) {
			_currentProjectionState = rootPartitionState;
			UpdateStateSizeMetrics(partition: string.Empty, rootPartitionState.Size);
		}

		_lastProcessedEventPosition.UpdateByCheckpointTagInitial(checkpointTag);
		_lastProcessedEventProgress = -1;
		_lastCompletedCheckpointPosition = checkpointTag;
		_requestedCheckpointPosition = null;
		CurrentCheckpoint = CreateProjectionCheckpoint(_lastProcessedEventPosition.LastTag);
		CurrentCheckpoint.Start();
	}

	public void Stopping() {
		EnsureStarted();
		if (_stopping)
			throw new InvalidOperationException("Already stopping");
		_stopping = true;
		RequestCheckpointToStop();
	}

	public void Stopped() {
		EnsureStarted();
		_started = false;
		_stopped = true;

		CurrentCheckpoint?.Dispose();
		CurrentCheckpoint = null;

		_closingCheckpoint?.Dispose();
		_closingCheckpoint = null;
	}

	public virtual void GetStatistics(ProjectionStatistics info) {
		info.Position = (_lastProcessedEventPosition.LastTag ?? (object)"").ToString();
		info.Progress = _lastProcessedEventProgress;
		info.LastCheckpoint = _lastCompletedCheckpointPosition != null
			? _lastCompletedCheckpointPosition.ToString()
			: "";
		info.EventsProcessedAfterRestart = _eventsProcessedAfterRestart;
		info.WritePendingEventsBeforeCheckpoint = _closingCheckpoint?.GetWritePendingEvents() ?? 0;
		info.WritePendingEventsAfterCheckpoint = CurrentCheckpoint?.GetWritePendingEvents() ?? 0;
		info.ReadsInProgress = /*_readDispatcher.ActiveRequestCount*/
			+ +(_closingCheckpoint?.GetReadsInProgress() ?? 0)
			+ (CurrentCheckpoint?.GetReadsInProgress() ?? 0);
		info.WritesInProgress = (_closingCheckpoint?.GetWritesInProgress() ?? 0)
		                        + (CurrentCheckpoint?.GetWritesInProgress() ?? 0);
		info.CheckpointStatus = _inCheckpoint ? "Requested" : "";

		foreach (var (partition, stateSize) in _stateSizeByPartition) {
			info.StateSizes ??= [];
			info.StateSizes[partition] = stateSize;
		}

		info.StateSizeThreshold = _maxProjectionStateSizeThreshold;
		info.StateSizeLimit = _maxProjectionStateSize;
	}

	public void StateUpdated(string partition, PartitionState oldState, PartitionState newState) {
		if (_stopped)
			return;
		EnsureStarted();
		if (_stopping)
			throw new InvalidOperationException("Stopping");

		if (partition == "" && newState.State == null) // ignore non-root partitions and non-changed states
			throw new NotSupportedException("Internal check");

		if (!CheckStateSize(newState, partition)) {
			return;
		}

		if (_usePersistentCheckpoints && partition != "")
			CapturePartitionStateUpdated(partition, oldState, newState);

		if (partition == "") {
			_currentProjectionState = newState;
			UpdateStateSizeMetrics(partition: string.Empty, newState.Size);
		}
	}

	private bool CheckStateSize(PartitionState result, string partition) {
		if (result.Size <= _maxProjectionStateSize) return true;
		var partitionMessage = partition == string.Empty ? string.Empty : $" in partition '{partition}'";
		Failed(
			$"The state size of projection '{NamingBuilder.EffectiveProjectionName}'{partitionMessage} is {result.Size:N0} bytes " +
			$"which exceeds the configured MaxProjectionStateSize of {_maxProjectionStateSize:N0} bytes.");
		return false;
	}

	public void EventProcessed(CheckpointTag checkpointTag, float progress) {
		if (_stopped)
			return;
		EnsureStarted();
		if (_stopping)
			throw new InvalidOperationException("Stopping");
		_eventsProcessedAfterRestart++;
		_lastProcessedEventPosition.UpdateByCheckpointTagForward(checkpointTag);
		_lastProcessedEventProgress = progress;
		// running state only
	}

	public void EventsEmitted(EmittedEventEnvelope[] scheduledWrites, Guid causedBy, string correlationId) {
		if (_stopped)
			return;
		EnsureStarted();
		if (_stopping)
			throw new InvalidOperationException("Stopping");
		if (scheduledWrites != null) {
			foreach (var @event in scheduledWrites) {
				var emittedEvent = @event.Event;
				if (string.IsNullOrEmpty(@event.Event.StreamId)) {
					Failed("Cannot write to a null stream id");
					return;
				}

				emittedEvent.SetCausedBy(causedBy);
				emittedEvent.SetCorrelationId(correlationId);
			}

			CurrentCheckpoint.ValidateOrderAndEmitEvents(scheduledWrites);
		}
	}

	public bool CheckpointSuggested(CheckpointTag checkpointTag, float progress) {
		if (!_usePersistentCheckpoints)
			throw new InvalidOperationException("Checkpoints are not used");
		if (_stopped || _stopping)
			return true;
		EnsureStarted();
		if (checkpointTag != _lastProcessedEventPosition.LastTag) // allow checkpoint at the current position
			_lastProcessedEventPosition.UpdateByCheckpointTagForward(checkpointTag);
		_lastProcessedEventProgress = progress;
		return RequestCheckpoint(_lastProcessedEventPosition);
	}

	public void Progress(float progress) {
		if (_stopping || _stopped)
			return;
		_lastProcessedEventProgress = progress < -1 ? -1 : progress;
	}

	public CheckpointTag LastProcessedEventPosition => _lastProcessedEventPosition.LastTag;

	public void Handle(CoreProjectionProcessingMessage.ReadyForCheckpoint message) {
		// ignore any messages - typically when faulted
		if (_stopped)
			return;
		// ignore any messages from previous checkpoints probably before RestartRequested
		if (message.Sender != _closingCheckpoint)
			return;
		if (!_inCheckpoint)
			throw new InvalidOperationException();
		if (_usePersistentCheckpoints)
			BeginWriteCheckpoint(_requestedCheckpointPosition, _requestedCheckpointState.Serialize());
		else
			CheckpointWritten(_requestedCheckpointPosition);
	}

	public void Handle(CoreProjectionProcessingMessage.RestartRequested message) {
		if (_stopped)
			return;
		RequestRestart(message.Reason);
	}

	public void Handle(CoreProjectionProcessingMessage.Failed message) {
		if (_stopped)
			return;
		Failed(message.Reason);
	}

	protected void PrerecordedEventsLoaded(CheckpointTag checkpointTag) {
		Publisher.Publish(new CoreProjectionProcessingMessage.PrerecordedEventsLoaded(_projectionCorrelationId, checkpointTag));
	}

	private void RequestCheckpointToStop() {
		EnsureStarted();
		if (!_stopping)
			throw new InvalidOperationException("Not stopping");
		if (_inCheckpoint) // checkpoint in progress.  no other writes will happen, so we can stop here.
			return;
		// do not request checkpoint if no events were processed since last checkpoint
		// NOTE: we ignore _usePersistentCheckpoints flag as we need to flush final writes before query object
		// has been disposed
		if (_lastCompletedCheckpointPosition < _lastProcessedEventPosition.LastTag) {
			RequestCheckpoint(_lastProcessedEventPosition);
			return;
		}

		Publisher.Publish(new CoreProjectionProcessingMessage.CheckpointCompleted(_projectionCorrelationId, _lastCompletedCheckpointPosition));
	}

	protected void EnsureStarted() {
		if (!_started)
			throw new InvalidOperationException("Not started");
	}

	/// <returns>true - if checkpoint has been completed in-sync</returns>
	private bool RequestCheckpoint(PositionTracker lastProcessedEventPosition) {
		return !_inCheckpoint
			? StartCheckpoint(lastProcessedEventPosition, _currentProjectionState)
			: throw new InvalidOperationException("Checkpoint in progress");
	}

	/// <returns>true - if checkpoint has been completed in-sync</returns>
	private bool StartCheckpoint(PositionTracker lastProcessedEventPosition, PartitionState projectionState) {
		ArgumentNullException.ThrowIfNull(projectionState);

		CheckpointTag requestedCheckpointPosition = lastProcessedEventPosition.LastTag;
		if (requestedCheckpointPosition == _lastCompletedCheckpointPosition)
			return true; // either suggested or requested to stop

		if (_usePersistentCheckpoints) // do not emit any events if we do not use persistent checkpoints
			EmitPartitionCheckpoints();

		_inCheckpoint = true;
		_requestedCheckpointPosition = requestedCheckpointPosition;
		_requestedCheckpointState = projectionState;
		_closingCheckpoint = CurrentCheckpoint;
		CurrentCheckpoint = CreateProjectionCheckpoint(requestedCheckpointPosition);
		// checkpoint only after assigning new current checkpoint, as it may call back immediately
		_closingCheckpoint.Prepare(requestedCheckpointPosition);
		return false; // even if prepare completes in sync it notifies the world by a message
	}

	protected void SendPrerecordedEvent(
		KurrentDB.Core.Data.ResolvedEvent pair,
		CheckpointTag positionTag,
		long prerecordedEventMessageSequenceNumber) {
		var committedEvent = new ReaderSubscriptionMessage.CommittedEventDistributed(
			Guid.Empty, new(pair, null), null, -1, source: GetType());
		Publisher.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.FromCommittedEventDistributed(
				committedEvent, positionTag, null, _projectionCorrelationId,
				prerecordedEventMessageSequenceNumber));
	}


	protected void RequestRestart(string reason) {
		_stopped = true; // ignore messages
		Publisher.Publish(new CoreProjectionProcessingMessage.RestartRequested(_projectionCorrelationId, reason));
	}

	private void Failed(string reason) {
		_stopped = true; // ignore messages
		Publisher.Publish(new CoreProjectionProcessingMessage.Failed(_projectionCorrelationId, reason));
	}

	protected void CheckpointWritten(CheckpointTag lastCompletedCheckpointPosition) {
		_lastCompletedCheckpointPosition = lastCompletedCheckpointPosition;
		_closingCheckpoint.Dispose();
		_closingCheckpoint = null;
		if (!_stopping)
			// ignore any writes pending in the current checkpoint (this is not the best, but they will never hit the storage, so it is safe)
			CurrentCheckpoint.Start();
		_inCheckpoint = false;

		//NOTE: the next checkpoint will start by completing checkpoint work item
		Publisher.Publish(
			new CoreProjectionProcessingMessage.CheckpointCompleted(
				_projectionCorrelationId, _lastCompletedCheckpointPosition));
	}

	protected void UpdateStateSizeMetrics(string partition, int newStateSize) {
		if (newStateSize >= _maxProjectionStateSizeThreshold) {
			// the new state size is above/equal to the threshold, we definitely need to report this in the metrics.
			_stateSizeByPartition[partition] = newStateSize;
		} else if (_stateSizeByPartition.TryGetValue(partition, out var oldStateSize)) {
			// the new state size is below the threshold, but it was recently above/equal to it
			// as it's still present in the dictionary

			if (oldStateSize >= _maxProjectionStateSizeThreshold) {
				// the old state size was above/equal to the threshold -
				// we provide a last update with a value that's below the threshold so that users
				// can observe the effects of any actions they have taken to reduce the state size.
				_stateSizeByPartition[partition] = newStateSize;
			} else {
				// the old state size was below to the threshold -
				// therefore the last update was already provided, and we now stop providing updates for this partition.
				// it'll automatically be removed from the metrics output after some time.
				_stateSizeByPartition.TryRemove(partition, out _);
			}
		}
	}

	public virtual void BeginLoadPrerecordedEvents(CheckpointTag checkpointTag) {
		PrerecordedEventsLoaded(checkpointTag);
	}
}
