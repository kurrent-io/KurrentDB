// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Projections.Core.Messages;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public class CoreProjectionCheckpointWriter {
	private readonly string _projectionCheckpointStreamId;
	private readonly ILogger _logger;
	private readonly IODispatcher _ioDispatcher;
	private readonly ProjectionVersion _projectionVersion;
	private readonly string _name;

	private Guid _writeRequestId;
	private int _inCheckpointWriteAttempt;
	private long _lastWrittenCheckpointEventNumber;
	private Event _checkpointEventToBePublished;
	private CheckpointTag _requestedCheckpointPosition;
	private IEnvelope _envelope;
	private const int MaxNumberOfRetries = 12;
	private const int MinAttemptWarnThreshold = 5;
	private bool _metaStreamWritten;
	private Random _random = new Random();
	private bool _largeCheckpointWarningLogged = false;

	public CoreProjectionCheckpointWriter(
		string projectionCheckpointStreamId, IODispatcher ioDispatcher, ProjectionVersion projectionVersion,
		string name) {
		_projectionCheckpointStreamId = projectionCheckpointStreamId;
		_logger = Log.ForContext<CoreProjectionCheckpointWriter>();
		_ioDispatcher = ioDispatcher;
		_projectionVersion = projectionVersion;
		_name = name;
	}

	public void BeginWriteCheckpoint(IEnvelope envelope,
		CheckpointTag requestedCheckpointPosition, string requestedCheckpointState) {
		_envelope = envelope;
		_requestedCheckpointPosition = requestedCheckpointPosition;
		_inCheckpointWriteAttempt = 1;
		//TODO: pass correct expected version
		_checkpointEventToBePublished = new Event(
			Guid.NewGuid(), ProjectionEventTypes.ProjectionCheckpoint, true,
			requestedCheckpointState == null ? null : Helper.UTF8NoBom.GetBytes(requestedCheckpointState),
			requestedCheckpointPosition.ToJsonBytes(projectionVersion: _projectionVersion), SchemaInfo.None, SchemaInfo.None);
		PublishWriteStreamMetadataAndCheckpointEventDelayed();
	}

	private void WriteCheckpointEventCompleted(
		string eventStreamId, OperationResult operationResult, long firstWrittenEventNumber) {
		if (_inCheckpointWriteAttempt == 0)
			throw new InvalidOperationException();
		if (operationResult == OperationResult.Success) {
			if (_logger != null)
				_logger.Verbose(
					"Checkpoint has been written for projection {projection} at sequence number {firstWrittenEventNumber} (current)",
					_name,
					firstWrittenEventNumber);
			_lastWrittenCheckpointEventNumber = firstWrittenEventNumber;

			_inCheckpointWriteAttempt = 0;
			_envelope.ReplyWith(
				new CoreProjectionCheckpointWriterMessage.CheckpointWritten(_requestedCheckpointPosition));
		} else {
			if (_logger != null) {
				_logger.Information(
					"Failed to write projection checkpoint to stream {stream}. Error: {e}", eventStreamId,
					Enum.GetName(typeof(OperationResult), operationResult));
			}

			switch (operationResult) {
				case OperationResult.WrongExpectedVersion:
					_envelope.ReplyWith(new CoreProjectionProcessingMessage.Failed(Guid.Empty,
						$"Checkpoint stream `{eventStreamId}` has been written to from the outside"
					));
					break;
				case OperationResult.PrepareTimeout:
				case OperationResult.ForwardTimeout:
				case OperationResult.CommitTimeout:
					if (_inCheckpointWriteAttempt >= MaxNumberOfRetries) {
						//The first parameter is not needed in this case as the CoreProjectionCheckpointManager takes care of filling in the projection id when it reconstructs the message
						_envelope.ReplyWith(new CoreProjectionProcessingMessage.Failed(Guid.Empty,
							string.Format(
								"After retrying {0} times, we failed to write the checkpoint for {1} to {2} due to a {3}",
								MaxNumberOfRetries, _name, eventStreamId,
								Enum.GetName(typeof(OperationResult), operationResult))));
						_inCheckpointWriteAttempt = 0;
						return;
					}

					_inCheckpointWriteAttempt++;
					PublishWriteStreamMetadataAndCheckpointEventDelayed();
					break;
				default:
					throw new NotSupportedException("Unsupported error code received");
			}
		}
	}

	private void PublishWriteStreamMetadataAndCheckpointEventDelayed() {
		var attempt = _inCheckpointWriteAttempt;
		var delayInSeconds = CalculateBackoffTimeSecs(attempt);
		if (delayInSeconds == 0)
			PublishWriteStreamMetadataAndCheckpointEvent();
		else {
			if (attempt >= MinAttemptWarnThreshold && _logger != null) {
				_logger.Warning("Attempt: {attempt} to write checkpoint for {projection} at {requestedCheckpointPosition} with expected version number {lastWrittenCheckpointEventNumber}. Backing off for {time} second(s).",
					attempt,
					_name,
					_requestedCheckpointPosition,
					_lastWrittenCheckpointEventNumber,
					delayInSeconds);
			}
			_ioDispatcher.Delay(
				TimeSpan.FromSeconds(delayInSeconds),
				_ => PublishWriteStreamMetadataAndCheckpointEvent());
		}
	}

	private void PublishWriteStreamMetadataAndCheckpointEvent() {
		if (_logger != null)
			_logger.Verbose(
				"Writing checkpoint for {projection} at {requestedCheckpointPosition} with expected version number {lastWrittenCheckpointEventNumber}",
				_name, _requestedCheckpointPosition, _lastWrittenCheckpointEventNumber);
		if (!_metaStreamWritten)
			PublishWriteStreamMetadata();
		else
			PublishWriteCheckpointEvent();
	}

	private void PublishWriteStreamMetadata() {
		var metaStreamId = SystemStreams.MetastreamOf(_projectionCheckpointStreamId);
		_writeRequestId = _ioDispatcher.WriteEvent(
			metaStreamId, ExpectedVersion.Any, CreateStreamMetadataEvent(), SystemAccounts.System, msg => {
				switch (msg.Result) {
					case OperationResult.Success:
						_metaStreamWritten = true;
						PublishWriteCheckpointEvent();
						break;
					default:
						WriteCheckpointEventCompleted(metaStreamId, msg.Result, ExpectedVersion.Invalid);
						break;
				}
			});
	}

	private Event CreateStreamMetadataEvent() {
		var eventId = Guid.NewGuid();
		var acl = new StreamAcl(
			readRole: SystemRoles.Admins, writeRole: SystemRoles.Admins,
			deleteRole: SystemRoles.Admins, metaReadRole: SystemRoles.All,
			metaWriteRole: SystemRoles.Admins);
		var metadata = new StreamMetadata(maxCount: 2, maxAge: null, cacheControl: null, acl: acl);
		var dataBytes = metadata.ToJsonBytes();
		return new Event(eventId, SystemEventTypes.StreamMetadata, isJson: true, data: dataBytes, metadata: null, SchemaInfo.None, SchemaInfo.None);
	}

	private void PublishWriteCheckpointEvent() {
		CheckpointSizeCheck();
		_writeRequestId = _ioDispatcher.WriteEvent(
			_projectionCheckpointStreamId, _lastWrittenCheckpointEventNumber, _checkpointEventToBePublished,
			SystemAccounts.System,
			msg => WriteCheckpointEventCompleted(_projectionCheckpointStreamId, msg.Result, msg.FirstEventNumber));
	}

	private void CheckpointSizeCheck() {
		if (!_largeCheckpointWarningLogged && _checkpointEventToBePublished.Data.Length >= 8_000_000) {
			Log.Warning(
				"Checkpoint size for the Projection {projectionName} is greater than 8 MB. Checkpoint size for a projection should be less than 16 MB. Current checkpoint size for Projection {projectionName} is {stateSize} MB.",
				_name, _name,
				_checkpointEventToBePublished.Data.Length / Math.Pow(10, 6));
			_largeCheckpointWarningLogged = true;
		}
	}

	public void Initialize() {
		_checkpointEventToBePublished = null;
		_inCheckpointWriteAttempt = 0;
		_ioDispatcher.Writer.Cancel(_writeRequestId);
		_lastWrittenCheckpointEventNumber = ExpectedVersion.Invalid;
		_metaStreamWritten = false;
	}

	public void GetStatistics(ProjectionStatistics info) {
		info.WritesInProgress = ((_inCheckpointWriteAttempt != 0) ? 1 : 0) + info.WritesInProgress;
		info.CheckpointStatus = _inCheckpointWriteAttempt > 0
			? "Writing (" + _inCheckpointWriteAttempt + ")"
			: info.CheckpointStatus;
	}

	public void StartFrom(CheckpointTag checkpointTag, long checkpointEventNumber) {
		_lastWrittenCheckpointEventNumber = checkpointEventNumber;
		_metaStreamWritten = checkpointEventNumber != ExpectedVersion.NoStream;
	}

	private int CalculateBackoffTimeSecs(int attempt) {
		attempt--;
		if (attempt == 0)
			return 0;
		var expBackoff = attempt < 9 ? (1 << attempt) : 256;
		return _random.Next(1, expBackoff + 1);
	}
}
