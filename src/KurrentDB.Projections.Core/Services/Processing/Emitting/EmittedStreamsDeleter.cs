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

public class EmittedStreamsDeleter(
	IODispatcher ioDispatcher,
	string emittedStreamsId,
	string emittedStreamsCheckpointStreamId)
	: IEmittedStreamsDeleter {
	private static readonly ILogger Log = Serilog.Log.ForContext<EmittedStreamsDeleter>();
	private const int CheckPointThreshold = 4000;
	private int _numberOfEventsProcessed;
	private const int RetryLimit = 3;
	private int _retryCount = RetryLimit;

	public void DeleteEmittedStreams(Action onEmittedStreamsDeleted) {
		ioDispatcher.ReadBackward(emittedStreamsCheckpointStreamId, -1, 1, false, SystemAccounts.System,
			result => {
				var deleteFromPosition = GetPositionToDeleteFrom(result);
				DeleteEmittedStreamsFrom(deleteFromPosition, onEmittedStreamsDeleted);
			},
			() => DeleteEmittedStreams(onEmittedStreamsDeleted),
			Guid.NewGuid());
	}

	private static int GetPositionToDeleteFrom(ClientMessage.ReadStreamEventsBackwardCompleted onReadCompleted) {
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
		ioDispatcher.ReadForward(emittedStreamsId, fromPosition, 1, false, SystemAccounts.System,
			x => ReadCompleted(x, onEmittedStreamsDeleted),
			() => DeleteEmittedStreamsFrom(fromPosition, onEmittedStreamsDeleted),
			Guid.NewGuid());
	}

	private void ReadCompleted(ClientMessage.ReadStreamEventsForwardCompleted onReadCompleted,
		Action onEmittedStreamsDeleted) {
		if (onReadCompleted.Result is ReadStreamResult.Success or ReadStreamResult.NoStream) {
			switch (onReadCompleted.Events) {
				case [] when !onReadCompleted.IsEndOfStream:
					DeleteEmittedStreamsFrom(onReadCompleted.NextEventNumber, onEmittedStreamsDeleted);
					return;
				case []:
					ioDispatcher.DeleteStream(emittedStreamsCheckpointStreamId, ExpectedVersion.Any, false,
						SystemAccounts.System, x => {
							switch (x.Result) {
								// currently, WrongExpectedVersion is returned when deleting non-existing streams, even when specifying ExpectedVersion.Any.
								// it is not too intuitive but changing the response would break the contract and compatibility with TCP/gRPC/web clients or require adding a new error code to all clients.
								// note: we don't need to check if CurrentVersion == -1 here to make sure it's a non-existing stream since the deletion is done with ExpectedVersion.Any
								case OperationResult.WrongExpectedVersion:
									// stream was never created
									Log.Information("PROJECTIONS: Projection Stream '{stream}' was not deleted since it does not exist", emittedStreamsCheckpointStreamId);
									break;
								case OperationResult.Success:
								case OperationResult.StreamDeleted:
									Log.Information("PROJECTIONS: Projection Stream '{stream}' deleted",
										emittedStreamsCheckpointStreamId);
									break;
								default:
									Log.Error("PROJECTIONS: Failed to delete projection stream '{stream}'. Reason: {e}",
										emittedStreamsCheckpointStreamId, x.Result);
									break;
							}

							ioDispatcher.DeleteStream(emittedStreamsId, ExpectedVersion.Any, false,
								SystemAccounts.System, y => {
									// currently, WrongExpectedVersion is returned when deleting non-existing streams, even when specifying ExpectedVersion.Any.
									// it is not too intuitive but changing the response would break the contract and compatibility with TCP/gRPC/web clients or require adding a new error code to all clients.
									// note: we don't need to check if CurrentVersion == -1 here to make sure it's a non-existing stream since the deletion is done with ExpectedVersion.Any
									if (x.Result == OperationResult.WrongExpectedVersion) {
										// stream was never created
										Log.Information("PROJECTIONS: Projection Stream '{stream}' was not deleted since it does not exist", emittedStreamsId);
									} else if (y.Result is OperationResult.Success or OperationResult.StreamDeleted) {
										Log.Information("PROJECTIONS: Projection Stream '{stream}' deleted",
											emittedStreamsId);
									} else {
										Log.Error(
											"PROJECTIONS: Failed to delete projection stream '{stream}'. Reason: {e}",
											emittedStreamsId, y.Result);
									}

									onEmittedStreamsDeleted();
								});
						});
					break;
				default: {
					var streamId = Helper.UTF8NoBom.GetString(onReadCompleted.Events[0].Event.Data.Span);
					ioDispatcher.DeleteStream(streamId, ExpectedVersion.Any, false, SystemAccounts.System,
						x => DeleteStreamCompleted(x, onEmittedStreamsDeleted, streamId,
							onReadCompleted.Events[0].OriginalEventNumber));
					break;
				}
			}
		}
	}

	private void DeleteStreamCompleted(ClientMessage.DeleteStreamCompleted deleteStreamCompleted,
		Action onEmittedStreamsDeleted, string streamId, long eventNumber) {
		if (deleteStreamCompleted.Result is OperationResult.Success or OperationResult.StreamDeleted) {
			_retryCount = RetryLimit;
			_numberOfEventsProcessed++;
			if (_numberOfEventsProcessed >= CheckPointThreshold) {
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
				streamId, RetryLimit - _retryCount + 1, RetryLimit, deleteStreamCompleted.Result);
			_retryCount--;
			DeleteEmittedStreamsFrom(eventNumber, onEmittedStreamsDeleted);
		}
	}

	private void TryMarkCheckpoint(long eventNumber) {
		ioDispatcher.WriteEvent(emittedStreamsCheckpointStreamId, ExpectedVersion.Any,
			new Event(Guid.NewGuid(), ProjectionEventTypes.PartitionCheckpoint, true, eventNumber.ToJson(), null),
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
