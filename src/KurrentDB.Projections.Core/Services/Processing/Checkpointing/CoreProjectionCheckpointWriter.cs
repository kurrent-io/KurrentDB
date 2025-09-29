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

public class CoreProjectionCheckpointWriter(
	string projectionCheckpointStreamId,
	IODispatcher ioDispatcher,
	ProjectionVersion projectionVersion,
	string name) {
	private static readonly ILogger Logger = Log.ForContext<CoreProjectionCheckpointWriter>();

	private Guid _writeRequestId;
	private int _inCheckpointWriteAttempt;
	private long _lastWrittenCheckpointEventNumber;
	private Event _checkpointEventToBePublished;
	private CheckpointTag _requestedCheckpointPosition;
	private IEnvelope _envelope;
	private const int MaxNumberOfRetries = 12;
	private const int MinAttemptWarnThreshold = 5;
	private bool _metaStreamWritten;
	private readonly Random _random = new();
	private bool _largeCheckpointWarningLogged;

	public void BeginWriteCheckpoint(IEnvelope envelope,
		CheckpointTag requestedCheckpointPosition, string requestedCheckpointState) {
		_envelope = envelope;
		_requestedCheckpointPosition = requestedCheckpointPosition;
		_inCheckpointWriteAttempt = 1;
		//TODO: pass correct expected version
		_checkpointEventToBePublished = new Event(
			Guid.NewGuid(), ProjectionEventTypes.ProjectionCheckpoint, true,
			requestedCheckpointState == null ? null : Helper.UTF8NoBom.GetBytes(requestedCheckpointState),
			isPropertyMetadata: false,
			requestedCheckpointPosition.ToJsonBytes(projectionVersion: projectionVersion));
		PublishWriteStreamMetadataAndCheckpointEventDelayed();
	}

	private void WriteCheckpointEventCompleted(
		string eventStreamId, OperationResult operationResult, long firstWrittenEventNumber) {
		if (_inCheckpointWriteAttempt == 0)
			throw new InvalidOperationException();
		if (operationResult == OperationResult.Success) {
			Logger?.Verbose(
				"Checkpoint has been written for projection {projection} at sequence number {firstWrittenEventNumber} (current)",
				name,
				firstWrittenEventNumber);
			_lastWrittenCheckpointEventNumber = firstWrittenEventNumber;

			_inCheckpointWriteAttempt = 0;
			_envelope.ReplyWith(new CoreProjectionCheckpointWriterMessage.CheckpointWritten(_requestedCheckpointPosition));
		} else {
			Logger?.Information(
				"Failed to write projection checkpoint to stream {stream}. Error: {e}", eventStreamId,
				Enum.GetName(typeof(OperationResult), operationResult));

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
							$"After retrying {MaxNumberOfRetries} times, we failed to write the checkpoint for {name} to {eventStreamId} due to a {Enum.GetName(typeof(OperationResult), operationResult)}"));
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
			if (attempt >= MinAttemptWarnThreshold && Logger != null) {
				Logger.Warning("Attempt: {attempt} to write checkpoint for {projection} at {requestedCheckpointPosition} with expected version number {lastWrittenCheckpointEventNumber}. Backing off for {time} second(s).",
					attempt,
					name,
					_requestedCheckpointPosition,
					_lastWrittenCheckpointEventNumber,
					delayInSeconds);
			}
			ioDispatcher.Delay(
				TimeSpan.FromSeconds(delayInSeconds),
				_ => PublishWriteStreamMetadataAndCheckpointEvent());
		}
	}

	private void PublishWriteStreamMetadataAndCheckpointEvent() {
		Logger?.Verbose(
			"Writing checkpoint for {projection} at {requestedCheckpointPosition} with expected version number {lastWrittenCheckpointEventNumber}",
			name, _requestedCheckpointPosition, _lastWrittenCheckpointEventNumber);
		if (!_metaStreamWritten)
			PublishWriteStreamMetadata();
		else
			PublishWriteCheckpointEvent();
	}

	private void PublishWriteStreamMetadata() {
		var metaStreamId = SystemStreams.MetastreamOf(projectionCheckpointStreamId);
		_writeRequestId = ioDispatcher.WriteEvent(
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

	private static Event CreateStreamMetadataEvent() {
		var acl = new StreamAcl(
			readRole: SystemRoles.Admins, writeRole: SystemRoles.Admins,
			deleteRole: SystemRoles.Admins, metaReadRole: SystemRoles.All,
			metaWriteRole: SystemRoles.Admins);
		var metadata = new StreamMetadata(maxCount: 2, maxAge: null, cacheControl: null, acl: acl);
		var dataBytes = metadata.ToJsonBytes();
		return new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, isJson: true, data: dataBytes);
	}

	private void PublishWriteCheckpointEvent() {
		CheckpointSizeCheck();
		_writeRequestId = ioDispatcher.WriteEvent(
			projectionCheckpointStreamId, _lastWrittenCheckpointEventNumber, _checkpointEventToBePublished,
			SystemAccounts.System,
			msg => WriteCheckpointEventCompleted(projectionCheckpointStreamId, msg.Result, msg.FirstEventNumbers.Single));
	}

	private void CheckpointSizeCheck() {
		if (!_largeCheckpointWarningLogged && _checkpointEventToBePublished.Data.Length >= 8_000_000) {
			Log.Warning(
				"Checkpoint size for the Projection {projectionName} is greater than 8 MB. Checkpoint size for a projection should be less than 16 MB. Current checkpoint size for Projection {projectionName} is {stateSize} MB.",
				name, name,
				_checkpointEventToBePublished.Data.Length / Math.Pow(10, 6));
			_largeCheckpointWarningLogged = true;
		}
	}

	public void Initialize() {
		_checkpointEventToBePublished = null;
		_inCheckpointWriteAttempt = 0;
		ioDispatcher.Writer.Cancel(_writeRequestId);
		_lastWrittenCheckpointEventNumber = ExpectedVersion.Invalid;
		_metaStreamWritten = false;
	}

	public void GetStatistics(ProjectionStatistics info) {
		info.WritesInProgress = (_inCheckpointWriteAttempt != 0 ? 1 : 0) + info.WritesInProgress;
		info.CheckpointStatus = _inCheckpointWriteAttempt > 0
			? $"Writing ({_inCheckpointWriteAttempt})"
			: info.CheckpointStatus;
	}

	public void StartFrom(long checkpointEventNumber) {
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
