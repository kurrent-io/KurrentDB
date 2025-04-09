// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public class EmittedStreamsDeleter : IEmittedStreamsDeleter {
	private static readonly ILogger Log = Serilog.Log.ForContext<EmittedStreamsDeleter>();
	private readonly IODispatcher _ioDispatcher;
	private readonly int _checkPointThreshold = 4000;
	private int _numberOfEventsProcessed = 0;
	private const int RetryLimit = 3;
	private int _retryCount = RetryLimit;
	private readonly string _emittedStreamsId;
	private readonly string _emittedStreamsCheckpointStreamId;

	public EmittedStreamsDeleter(IODispatcher ioDispatcher, string emittedStreamsId,
		string emittedStreamsCheckpointStreamId) {
		_ioDispatcher = ioDispatcher;
		_emittedStreamsId = emittedStreamsId;
		_emittedStreamsCheckpointStreamId = emittedStreamsCheckpointStreamId;
	}

	public void DeleteEmittedStreams(Action onEmittedStreamsDeleted) {
		_ioDispatcher.ReadBackward(_emittedStreamsCheckpointStreamId, -1, 1, false, SystemAccounts.System,
			result => {
				var deleteFromPosition = GetPositionToDeleteFrom(result);
				DeleteEmittedStreamsFrom(deleteFromPosition, onEmittedStreamsDeleted);
			},
			() => DeleteEmittedStreams(onEmittedStreamsDeleted),
			Guid.NewGuid());
	}

	private int GetPositionToDeleteFrom(ClientMessage.ReadStreamEventsBackwardCompleted onReadCompleted) {
		int deleteFromPosition = 0;
		if (onReadCompleted.Result is ReadStreamResult.Success) {
			if (onReadCompleted.Events is not []) {
				var checkpoint = onReadCompleted.Events
					.Where(v => v.Event.EventType == ProjectionEventTypes.ProjectionCheckpoint).Select(x => x.Event)
					.FirstOrDefault();
				if (checkpoint != null) {
					deleteFromPosition = checkpoint.Data.ParseJson<int>();
				}
			}
		}

		return deleteFromPosition;
	}

	private void DeleteEmittedStreamsFrom(long fromPosition, Action onEmittedStreamsDeleted) {
		_ioDispatcher.ReadForward(_emittedStreamsId, fromPosition, 1, false, SystemAccounts.System,
			x => ReadCompleted(x, onEmittedStreamsDeleted),
			() => DeleteEmittedStreamsFrom(fromPosition, onEmittedStreamsDeleted),
			Guid.NewGuid());
	}

	private void ReadCompleted(ClientMessage.ReadStreamEventsForwardCompleted onReadCompleted,
		Action onEmittedStreamsDeleted) {
		if (onReadCompleted.Result is ReadStreamResult.Success or ReadStreamResult.NoStream) {
			if (onReadCompleted.Events is [] && !onReadCompleted.IsEndOfStream) {
				DeleteEmittedStreamsFrom(onReadCompleted.NextEventNumber, onEmittedStreamsDeleted);
				return;
			}

			if (onReadCompleted.Events is []) {
				_ioDispatcher.DeleteStream(_emittedStreamsCheckpointStreamId, ExpectedVersion.Any, false,
					SystemAccounts.System, x => {
						// currently, WrongExpectedVersion is returned when deleting non-existing streams, even when specifying ExpectedVersion.Any.
						// it is not too intuitive but changing the response would break the contract and compatibility with TCP/gRPC/web clients or require adding a new error code to all clients.
						// note: we don't need to check if CurrentVersion == -1 here to make sure it's a non-existing stream since the deletion is done with ExpectedVersion.Any
						if (x.Result == OperationResult.WrongExpectedVersion) {
							// stream was never created
							Log.Information("PROJECTIONS: Projection Stream '{stream}' was not deleted since it does not exist", _emittedStreamsCheckpointStreamId);
						} else if (x.Result == OperationResult.Success || x.Result == OperationResult.StreamDeleted) {
							Log.Information("PROJECTIONS: Projection Stream '{stream}' deleted",
								_emittedStreamsCheckpointStreamId);
						} else {
							Log.Error("PROJECTIONS: Failed to delete projection stream '{stream}'. Reason: {e}",
								_emittedStreamsCheckpointStreamId, x.Result);
						}

						_ioDispatcher.DeleteStream(_emittedStreamsId, ExpectedVersion.Any, false,
							SystemAccounts.System, y => {
								// currently, WrongExpectedVersion is returned when deleting non-existing streams, even when specifying ExpectedVersion.Any.
								// it is not too intuitive but changing the response would break the contract and compatibility with TCP/gRPC/web clients or require adding a new error code to all clients.
								// note: we don't need to check if CurrentVersion == -1 here to make sure it's a non-existing stream since the deletion is done with ExpectedVersion.Any
								if (x.Result == OperationResult.WrongExpectedVersion) {
									// stream was never created
									Log.Information("PROJECTIONS: Projection Stream '{stream}' was not deleted since it does not exist", _emittedStreamsId);
								} else if (y.Result == OperationResult.Success ||
										   y.Result == OperationResult.StreamDeleted) {
									Log.Information("PROJECTIONS: Projection Stream '{stream}' deleted",
										_emittedStreamsId);
								} else {
									Log.Error(
										"PROJECTIONS: Failed to delete projection stream '{stream}'. Reason: {e}",
										_emittedStreamsId, y.Result);
								}

								onEmittedStreamsDeleted();
							});
					});
			} else {
				var streamId = Helper.UTF8NoBom.GetString(onReadCompleted.Events[0].Event.Data.Span);
				_ioDispatcher.DeleteStream(streamId, ExpectedVersion.Any, false, SystemAccounts.System,
					x => DeleteStreamCompleted(x, onEmittedStreamsDeleted, streamId,
						onReadCompleted.Events[0].OriginalEventNumber));
			}
		}
	}

	private void DeleteStreamCompleted(ClientMessage.DeleteStreamCompleted deleteStreamCompleted,
		Action onEmittedStreamsDeleted, string streamId, long eventNumber) {
		if (deleteStreamCompleted.Result == OperationResult.Success ||
			deleteStreamCompleted.Result == OperationResult.StreamDeleted) {
			_retryCount = RetryLimit;
			_numberOfEventsProcessed++;
			if (_numberOfEventsProcessed >= _checkPointThreshold) {
				_numberOfEventsProcessed = 0;
				TryMarkCheckpoint(eventNumber);
			}

			DeleteEmittedStreamsFrom(eventNumber + 1, onEmittedStreamsDeleted);
		} else {
			if (_retryCount == 0) {
				Log.Error(
					"PROJECTIONS: Retry limit reached, could not delete stream: {stream}. Manual intervention is required and you may need to delete this stream manually",
					streamId);
				_retryCount = RetryLimit;
				DeleteEmittedStreamsFrom(eventNumber + 1, onEmittedStreamsDeleted);
				return;
			}

			Log.Error(
				"PROJECTIONS: Failed to delete emitted stream {stream}, Retrying ({retryCount}/{maxRetryCount}). Reason: {reason}",
				streamId, (RetryLimit - _retryCount) + 1, RetryLimit, deleteStreamCompleted.Result);
			_retryCount--;
			DeleteEmittedStreamsFrom(eventNumber, onEmittedStreamsDeleted);
		}
	}

	private void TryMarkCheckpoint(long eventNumber) {
		_ioDispatcher.WriteEvent(_emittedStreamsCheckpointStreamId, ExpectedVersion.Any,
			new Event(Guid.NewGuid(), ProjectionEventTypes.PartitionCheckpoint, true, eventNumber.ToJson(), null, SchemaInfo.None, SchemaInfo.None),
			SystemAccounts.System, x => {
				if (x.Result == OperationResult.Success) {
					Log.Debug("PROJECTIONS: Emitted Stream Deletion Checkpoint written at {eventNumber}",
						eventNumber);
				} else {
					Log.Debug(
						"PROJECTIONS: Emitted Stream Deletion Checkpoint Failed to be written at {eventNumber}",
						eventNumber);
				}
			});
	}
}
