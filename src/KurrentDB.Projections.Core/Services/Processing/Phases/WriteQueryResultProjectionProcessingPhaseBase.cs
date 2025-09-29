// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;

namespace KurrentDB.Projections.Core.Services.Processing.Phases;

public abstract class WriteQueryResultProjectionProcessingPhaseBase : IProjectionProcessingPhase {
	private readonly IPublisher _publisher;
	private readonly int _phase;
	protected readonly string _resultStream;
	private readonly ICoreProjectionForProcessingPhase _coreProjection;
	protected readonly PartitionStateCache _stateCache;
	private readonly IEmittedEventWriter _emittedEventWriter;
	private bool _subscribed;
	private PhaseState _projectionState;

	protected WriteQueryResultProjectionProcessingPhaseBase(
		IPublisher publisher,
		int phase,
		string resultStream,
		ICoreProjectionForProcessingPhase coreProjection,
		PartitionStateCache stateCache,
		ICoreProjectionCheckpointManager checkpointManager,
		IEmittedEventWriter emittedEventWriter,
		IEmittedStreamsTracker emittedStreamsTracker) {
		ArgumentException.ThrowIfNullOrEmpty(resultStream);
		ArgumentNullException.ThrowIfNull(coreProjection);
		ArgumentNullException.ThrowIfNull(stateCache);
		ArgumentNullException.ThrowIfNull(checkpointManager);
		ArgumentNullException.ThrowIfNull(emittedEventWriter);
		ArgumentNullException.ThrowIfNull(emittedStreamsTracker);

		_publisher = publisher;
		_phase = phase;
		_resultStream = resultStream;
		_coreProjection = coreProjection;
		_stateCache = stateCache;
		CheckpointManager = checkpointManager;
		_emittedEventWriter = emittedEventWriter;
		EmittedStreamsTracker = emittedStreamsTracker;
	}

	public ICoreProjectionCheckpointManager CheckpointManager { get; }

	public IEmittedStreamsTracker EmittedStreamsTracker { get; }

	public void Dispose() {
	}

	public void Handle(CoreProjectionManagementMessage.GetState message) {
		var state = _stateCache.TryGetPartitionState(message.Partition);
		var stateString = state?.State;
		_publisher.Publish(
			new CoreProjectionStatusMessage.StateReport(
				message.CorrelationId,
				message.CorrelationId,
				message.Partition,
				state: stateString,
				position: null));
	}

	public void Handle(CoreProjectionManagementMessage.GetResult message) {
		var state = _stateCache.TryGetPartitionState(message.Partition);
		var resultString = state?.Result;
		_publisher.Publish(
			new CoreProjectionStatusMessage.ResultReport(
				message.CorrelationId,
				message.CorrelationId,
				message.Partition,
				result: resultString,
				position: null));
	}

	public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
		throw new NotImplementedException();
	}

	public CheckpointTag AdjustTag(CheckpointTag tag) {
		return tag;
	}

	public void InitializeFromCheckpoint(CheckpointTag checkpointTag) {
		_subscribed = false;
	}

	public void ProcessEvent() {
		if (!_subscribed)
			throw new InvalidOperationException();
		if (_projectionState != PhaseState.Running)
			return;

		var phaseCheckpointTag = CheckpointTag.FromPhase(_phase, completed: true);
		var writeResults = WriteResults(phaseCheckpointTag);

		var writeEofResults = WriteEofEvent(phaseCheckpointTag);

		_emittedEventWriter.EventsEmitted(writeResults.Concat(writeEofResults).ToArray(), Guid.Empty, null);

		CheckpointManager.EventProcessed(phaseCheckpointTag, 100.0f);
		_coreProjection.CompletePhase();
	}

	private IEnumerable<EmittedEventEnvelope> WriteEofEvent(CheckpointTag phaseCheckpointTag) {
		yield return
			new EmittedEventEnvelope(
				new EmittedDataEvent(
					_resultStream,
					Guid.NewGuid(),
					"$Eof",
					true,
					null,
					null,
					phaseCheckpointTag,
					null));
	}

	protected abstract IEnumerable<EmittedEventEnvelope> WriteResults(CheckpointTag phaseCheckpointTag);

	public void Subscribe(CheckpointTag from, bool fromCheckpoint) {
		_subscribed = true;
		_coreProjection.Subscribed();
	}

	public void SetProjectionState(PhaseState state) {
		_projectionState = state;
	}

	public void GetStatistics(ProjectionStatistics info) {
		info.Status += "/Writing results";
	}

	public CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.FromPhase(_phase, completed: false);
	}

	public void EnsureUnsubscribed() {
	}
}
