// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Services.Processing.Checkpointing;

public class CoreProjectionCheckpointReader : ICoreProjectionCheckpointReader {
	private readonly IPublisher _publisher;
	private readonly Guid _projectionCorrelationId;
	private readonly IODispatcher _ioDispatcher;
	private readonly string _projectionCheckpointStreamId;
	private readonly bool _useCheckpoints;
	private readonly ILogger _logger = Serilog.Log.ForContext<CoreProjectionCheckpointReader>();

	private bool _stateRequested;

	private long _nextStateIndexToRequest;
	private ProjectionVersion _projectionVersion;
	private Guid _readRequestId;
	private long _lastWrittenCheckpointEventNumber;

	public CoreProjectionCheckpointReader(
		IPublisher publisher, Guid projectionCorrelationId, IODispatcher ioDispatcher,
		string projectionCheckpointStreamId, ProjectionVersion projectionVersion, bool useCheckpoints) {
		_publisher = publisher;
		_projectionCorrelationId = projectionCorrelationId;
		_ioDispatcher = ioDispatcher;
		_projectionCheckpointStreamId = projectionCheckpointStreamId;
		_projectionVersion = projectionVersion;
		_useCheckpoints = useCheckpoints;
	}

	public void BeginLoadState() {
		if (_stateRequested)
			throw new InvalidOperationException("State has been already requested");
		BeforeBeginLoadState();
		_stateRequested = true;
		if (_useCheckpoints) {
			RequestLoadState();
		} else {
			CheckpointLoaded(null, null);
		}
	}

	public void Initialize() {
		_ioDispatcher.BackwardReader.Cancel(_readRequestId);
		_readRequestId = Guid.Empty;
		_stateRequested = false;
	}

	protected void BeforeBeginLoadState() {
		_lastWrittenCheckpointEventNumber = ExpectedVersion.NoStream;
		_nextStateIndexToRequest = -1; // from the end
	}

	protected void RequestLoadState() {
		const int recordsToRequest = 10;
		_readRequestId = Guid.NewGuid();
		_ioDispatcher.ReadBackward(
			_projectionCheckpointStreamId, _nextStateIndexToRequest, recordsToRequest, false,
			SystemAccounts.System, OnLoadStateReadRequestCompleted,
			() => {
				_logger.Warning("Read forward of stream {stream} timed out. Retrying.", _projectionCheckpointStreamId);
				RequestLoadState();
			}, _readRequestId);
	}


	private void OnLoadStateReadRequestCompleted(ClientMessage.ReadStreamEventsBackwardCompleted message) {
		if (message.Events is not []) {
			var checkpoint = message.Events
				.Where(v => v.Event.EventType == ProjectionEventTypes.ProjectionCheckpoint).Select(x => x.Event)
				.FirstOrDefault();
			if (checkpoint != null) {
				var parsed = checkpoint.Metadata.ParseCheckpointTagVersionExtraJson(_projectionVersion);
				if (parsed.Version.ProjectionId != _projectionVersion.ProjectionId
				    || _projectionVersion.Epoch > parsed.Version.Version) {
					_lastWrittenCheckpointEventNumber = checkpoint.EventNumber;
					CheckpointLoaded(null, null);
				} else {
					//TODO: check epoch and correctly set _lastWrittenCheckpointEventNumber
					var checkpointData = Helper.UTF8NoBom.GetString(checkpoint.Data.Span);
					_lastWrittenCheckpointEventNumber = checkpoint.EventNumber;
					var adjustedTag = parsed.Tag; // the same projection and epoch, handle upgrades internally
					CheckpointLoaded(adjustedTag, checkpointData);
				}

				return;
			}
		}

		if (message.NextEventNumber != -1) {
			_nextStateIndexToRequest = message.NextEventNumber;
			RequestLoadState();
			return;
		}

		_lastWrittenCheckpointEventNumber = message.LastEventNumber;
		CheckpointLoaded(null, null);
	}


	protected void CheckpointLoaded(CheckpointTag checkpointTag, string checkpointData) {
		if (checkpointTag == null) // no checkpoint data found
		{
			checkpointData = null;
		}

		_publisher.Publish(
			new CoreProjectionProcessingMessage.CheckpointLoaded(
				_projectionCorrelationId, checkpointTag, checkpointData, _lastWrittenCheckpointEventNumber));
	}
}
