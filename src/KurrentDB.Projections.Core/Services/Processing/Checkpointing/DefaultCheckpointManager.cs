// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public class DefaultCheckpointManager : CoreProjectionCheckpointManager,
	IHandle<CoreProjectionCheckpointWriterMessage.CheckpointWritten>,
	IHandle<CoreProjectionCheckpointWriterMessage.RestartRequested> {
	private readonly ClaimsPrincipal _runAs;
	private readonly CheckpointTag _zeroTag;
	private int _readRequestsInProgress;
	private readonly HashSet<Guid> _loadStateRequests = new HashSet<Guid>();

	protected readonly ProjectionVersion _projectionVersion;
	protected readonly IODispatcher _ioDispatcher;
	private readonly PositionTagger _positionTagger;
	private readonly CoreProjectionCheckpointWriter _coreProjectionCheckpointWriter;
	private PartitionStateUpdateManager _partitionStateUpdateManager;

	public DefaultCheckpointManager(
		IPublisher publisher, Guid projectionCorrelationId, ProjectionVersion projectionVersion, ClaimsPrincipal runAs,
		IODispatcher ioDispatcher, ProjectionConfig projectionConfig, string name, PositionTagger positionTagger,
		ProjectionNamesBuilder namingBuilder, bool usePersistentCheckpoints, bool producesRunningResults,
		bool definesFold,
		CoreProjectionCheckpointWriter coreProjectionCheckpointWriter, int maxProjectionStateSize)
		: base(
			publisher, projectionCorrelationId, projectionConfig, name, positionTagger, namingBuilder,
			usePersistentCheckpoints, maxProjectionStateSize) {
		if (ioDispatcher == null)
			throw new ArgumentNullException("ioDispatcher");
		_projectionVersion = projectionVersion;
		_runAs = runAs;
		_ioDispatcher = ioDispatcher;
		_positionTagger = positionTagger;
		_coreProjectionCheckpointWriter = coreProjectionCheckpointWriter;
		_zeroTag = positionTagger.MakeZeroCheckpointTag();
	}

	protected override void BeginWriteCheckpoint(
		CheckpointTag requestedCheckpointPosition, string requestedCheckpointState) {
		_requestedCheckpointPosition = requestedCheckpointPosition;
		_coreProjectionCheckpointWriter.BeginWriteCheckpoint(
			new SendToThisEnvelope(this), requestedCheckpointPosition, requestedCheckpointState);
	}

	public override void RecordEventOrder(
		ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action committed) {
		committed();
	}

	public override void PartitionCompleted(string partition) {
		_partitionStateUpdateManager.PartitionCompleted(partition);
	}

	public override void Initialize() {
		base.Initialize();
		_partitionStateUpdateManager = null;
		foreach (var requestId in _loadStateRequests)
			_ioDispatcher.BackwardReader.Cancel(requestId);
		_loadStateRequests.Clear();
		_coreProjectionCheckpointWriter.Initialize();
		_requestedCheckpointPosition = null;
		_readRequestsInProgress = 0;
	}

	public override void GetStatistics(ProjectionStatistics info) {
		base.GetStatistics(info);
		info.ReadsInProgress += _readRequestsInProgress;
		_coreProjectionCheckpointWriter.GetStatistics(info);
	}

	public override void BeginLoadPartitionStateAt(
		string statePartition, CheckpointTag requestedStateCheckpointTag, Action<PartitionState> loadCompleted) {
		var stateEventType = ProjectionEventTypes.PartitionCheckpoint;
		var partitionCheckpointStreamName = _namingBuilder.MakePartitionCheckpointStreamName(statePartition);

		ReadPartitionStream(partitionCheckpointStreamName, -1, requestedStateCheckpointTag, loadCompleted,
			stateEventType);
	}

	private void ReadPartitionStream(string partitionStreamName, long eventNumber,
		CheckpointTag requestedStateCheckpointTag,
		Action<PartitionState> loadCompleted, string stateEventType) {
		_readRequestsInProgress++;
		var requestId = Guid.NewGuid();
		_ioDispatcher.ReadBackward(
			partitionStreamName, eventNumber, 1, false, SystemAccounts.System,
			m =>
				OnLoadPartitionStateReadStreamEventsBackwardCompleted(
					m, requestedStateCheckpointTag, loadCompleted, partitionStreamName, stateEventType),
			() => {
				_logger.Warning("Read backward for stream {stream} timed out. Retrying", partitionStreamName);
				_loadStateRequests.Remove(requestId);
				_readRequestsInProgress--;
				ReadPartitionStream(partitionStreamName, eventNumber, requestedStateCheckpointTag, loadCompleted,
					stateEventType);
			}, requestId);
		if (requestId != Guid.Empty)
			_loadStateRequests.Add(requestId);
	}

	private void OnLoadPartitionStateReadStreamEventsBackwardCompleted(
		ClientMessage.ReadStreamEventsBackwardCompleted message, CheckpointTag requestedStateCheckpointTag,
		Action<PartitionState> loadCompleted, string partitionStreamName, string stateEventType) {
		_loadStateRequests.Remove(message.CorrelationId);

		_readRequestsInProgress--;
		if (message.Events.Count is 1) {
			EventRecord @event = message.Events[0].Event;
			if (@event.EventType == stateEventType) {
				var parsed = @event.Metadata.ParseCheckpointTagVersionExtraJson(_projectionVersion);
				if (parsed.Version.ProjectionId != _projectionVersion.ProjectionId
					|| _projectionVersion.Epoch > parsed.Version.Version) {
					var state = new PartitionState("", null, _zeroTag);
					loadCompleted(state);
					return;
				} else {
					var loadedStateCheckpointTag = parsed.AdjustBy(_positionTagger, _projectionVersion);
					var state = PartitionState.Deserialize(
						Helper.UTF8NoBom.GetString(@event.Data.Span), loadedStateCheckpointTag);
					loadCompleted(state);
					return;
				}
			}
		}

		if (message.NextEventNumber == -1) {
			var state = new PartitionState("", null, _zeroTag);
			loadCompleted(state);
			return;
		}

		ReadPartitionStream(partitionStreamName, message.NextEventNumber, requestedStateCheckpointTag,
			loadCompleted, stateEventType);
	}

	protected override ProjectionCheckpoint CreateProjectionCheckpoint(CheckpointTag checkpointPosition) {
		return new ProjectionCheckpoint(
			_publisher, _ioDispatcher, _projectionVersion, _runAs, this, checkpointPosition, _positionTagger,
			_projectionConfig.MaxWriteBatchLength, _projectionConfig.MaximumAllowedWritesInFlight, _logger);
	}

	public void Handle(CoreProjectionCheckpointWriterMessage.CheckpointWritten message) {
		CheckpointWritten(message.Position);
	}

	public void Handle(CoreProjectionCheckpointWriterMessage.RestartRequested message) {
		RequestRestart(message.Reason);
	}

	protected override void CapturePartitionStateUpdated(string partition, PartitionState oldState,
		PartitionState newState) {
		if (_partitionStateUpdateManager == null)
			_partitionStateUpdateManager = new PartitionStateUpdateManager(_namingBuilder);
		_partitionStateUpdateManager.StateUpdated(partition, newState, oldState.CausedBy);

		UpdateStateSizeMetrics(partition, newState.Size);
	}

	protected override void EmitPartitionCheckpoints() {
		if (_partitionStateUpdateManager != null) {
			_partitionStateUpdateManager.EmitEvents(_currentCheckpoint);
			_partitionStateUpdateManager = null;
		}
	}
}
