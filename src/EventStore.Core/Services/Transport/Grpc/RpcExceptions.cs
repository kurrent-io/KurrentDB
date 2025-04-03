// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc;

public static class RpcExceptions {
	public static Exception Timeout(string message) => new RpcException(new Status(StatusCode.Aborted, $"Operation timed out: {message}"));

	public static RpcException ServerNotReady() =>
		new(new Status(StatusCode.Unavailable, "Server Is Not Ready"));

	public static RpcException ServerBusy() =>
		new(new Status(StatusCode.Unavailable, "Server Is Too Busy"));

	public static Exception NoLeaderInfo() =>
		new RpcException(new Status(StatusCode.Unknown, "No leader info available in response"));

	public static RpcException LeaderInfo(string host, int port) =>
		new(new Status(StatusCode.NotFound, $"Leader info available"), new Metadata {
			{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.NotLeader },
			{ Constants.Exceptions.LeaderEndpointHost, host },
			{ Constants.Exceptions.LeaderEndpointPort, port.ToString() },
		});

	public static RpcException StreamNotFound(string streamName) =>
		new(new Status(StatusCode.NotFound, $"Event stream '{streamName}' is not found."), new Metadata {
			{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.StreamNotFound },
			{ Constants.Exceptions.StreamName, streamName }
		});

	public static RpcException NoStream(string streamName) =>
		new(new Status(StatusCode.NotFound, $"Event stream '{streamName}' was not created."));

	public static RpcException UnknownMessage<T>(Message message) where T : Message =>
		UnknownMessage(message.GetType(), typeof(T));

	public static RpcException UnknownMessage(Type unknownMessageType, Type expectedMsgType) =>
		new(new Status(StatusCode.Unknown,
			$"Envelope callback expected either {expectedMsgType.Name} or {nameof(ClientMessage.NotHandled)}, received {unknownMessageType.Name} instead"));

	public static RpcException UnknownError<T>(T result) where T : unmanaged =>
		UnknownError(typeof(T), result);

	public static RpcException UnknownError(Type resultType, object result) =>
		new(new Status(StatusCode.Unknown, $"Unexpected {resultType.Name}: {result}"));

	public static RpcException UnknownError(string message) =>
		new(new Status(StatusCode.Unknown, message));

	public static RpcException AccessDenied() =>
		new(new Status(StatusCode.PermissionDenied, "Access Denied"), new Metadata {
			{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.AccessDenied }
		});

	public static RpcException InvalidTransaction() =>
		new(new Status(StatusCode.InvalidArgument, "Invalid Transaction"), new Metadata {
			{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.InvalidTransaction }
		});

	public static RpcException StreamDeleted(string streamName) =>
		new(new Status(StatusCode.FailedPrecondition, $"Event stream '{streamName}' is deleted."),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.StreamDeleted },
				{ Constants.Exceptions.StreamName, streamName }
			});

	public static RpcException ScavengeNotFound(string scavengeId) =>
		new(new Status(StatusCode.NotFound, "Scavenge id was invalid."),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.ScavengeNotFound },
				{ Constants.Exceptions.ScavengeId, scavengeId ?? string.Empty }
			});

	public static RpcException RedactionLockFailed() =>
		new(new Status(StatusCode.FailedPrecondition, "Failed to acquire lock for redaction."),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.RedactionLockFailed }
			});

	public static RpcException RedactionGetEventPositionFailed(string reason) =>
		new(new Status(StatusCode.Unknown, $"Failed to get event position: '{reason}'"),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.RedactionGetEventPositionFailed },
				{ Constants.Exceptions.Reason, reason }
			});

	public static RpcException RedactionSwitchChunkFailed(string reason) =>
		new(new Status(StatusCode.FailedPrecondition, $"Failed to switch chunk during redaction: '{reason}'"),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.RedactionSwitchChunkFailed },
				{ Constants.Exceptions.Reason, reason }
			});

	public static RpcException WrongExpectedVersion(
		string operation,
		string streamName,
		long expectedVersion,
		long? actualVersion = default) =>
		new(
			new Status(
				StatusCode.FailedPrecondition,
				$"{operation} failed due to WrongExpectedVersion. Stream: {streamName}, Expected version: {expectedVersion}, Actual version: {actualVersion}"),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.WrongExpectedVersion },
				{ Constants.Exceptions.StreamName, streamName },
				{ Constants.Exceptions.ExpectedVersion, expectedVersion.ToString() },
				{ Constants.Exceptions.ActualVersion, actualVersion?.ToString() ?? string.Empty }
			});

	public static RpcException MaxAppendEventSizeExceeded(string eventId, int proposedEventSize, int maxAppendEventSize) =>
		new(
			new Status(StatusCode.InvalidArgument, $"Event with Id: {eventId}, Size: {proposedEventSize}, exceeds Maximum Append Event Size of {maxAppendEventSize}."),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.MaximumAppendEventSizeExceeded },
				{ Constants.Exceptions.MaximumAppendEventSize, maxAppendEventSize.ToString() },
				{ Constants.Exceptions.EventId, eventId },
				{ Constants.Exceptions.ProposedAppendEventSize, proposedEventSize.ToString() }
			});

	public static RpcException MaxAppendSizeExceeded(int maxAppendSize) =>
		new(
			new Status(StatusCode.InvalidArgument, $"Maximum Append Size of {maxAppendSize} Exceeded."),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.MaximumAppendSizeExceeded },
				{ Constants.Exceptions.MaximumAppendSize, maxAppendSize.ToString() }
			});

	public static RpcException RequiredMetadataPropertyMissing(string missingMetadataProperty) =>
		new(
			new Status(StatusCode.InvalidArgument, $"Required Metadata Property '{missingMetadataProperty}' is missing"),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.MissingRequiredMetadataProperty },
				{ Constants.Exceptions.RequiredMetadataProperties, string.Join(",", Constants.Metadata.RequiredMetadata) }
			});

	public static bool TryHandleNotHandled(ClientMessage.NotHandled notHandled, out Exception exception) {
		exception = null;
		switch (notHandled.Reason) {
			case ClientMessage.NotHandled.Types.NotHandledReason.NotReady:
				exception = ServerNotReady();
				return true;
			case ClientMessage.NotHandled.Types.NotHandledReason.TooBusy:
				exception = ServerBusy();
				return true;
			case ClientMessage.NotHandled.Types.NotHandledReason.NotLeader:
			case ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly:
				switch (notHandled.LeaderInfo) {
					case { } leaderInfo:
						exception = LeaderInfo(leaderInfo.Http.GetHost(), leaderInfo.Http.GetPort());
						return true;
					default:
						exception = NoLeaderInfo();
						return true;
				}

			default:
				return false;
		}
	}

	public static Exception PersistentSubscriptionFailed(string streamName, string groupName, string reason)
		=> new RpcException(
			new Status(
				StatusCode.Internal,
				$"Subscription group {groupName} on stream {streamName} failed: '{reason}'"), new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.PersistentSubscriptionFailed },
				{ Constants.Exceptions.StreamName, streamName },
				{ Constants.Exceptions.GroupName, groupName },
				{ Constants.Exceptions.Reason, reason }
			});

	public static Exception PersistentSubscriptionDoesNotExist(string streamName, string groupName)
		=> new RpcException(
			new Status(
				StatusCode.NotFound,
				$"Subscription group {groupName} on stream {streamName} does not exist."), new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.PersistentSubscriptionDoesNotExist },
				{ Constants.Exceptions.StreamName, streamName },
				{ Constants.Exceptions.GroupName, groupName }
			});

	public static Exception PersistentSubscriptionExists(string streamName, string groupName)
		=> new RpcException(
			new Status(
				StatusCode.AlreadyExists,
				$"Subscription group {groupName} on stream {streamName} exists."), new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.PersistentSubscriptionExists },
				{ Constants.Exceptions.StreamName, streamName },
				{ Constants.Exceptions.GroupName, groupName }
			});

	public static Exception PersistentSubscriptionMaximumSubscribersReached(string streamName, string groupName)
		=> new RpcException(
			new Status(
				StatusCode.FailedPrecondition,
				$"Maximum subscriptions reached for subscription group {groupName} on stream {streamName}."),
			new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.MaximumSubscribersReached },
				{ Constants.Exceptions.StreamName, streamName },
				{ Constants.Exceptions.GroupName, groupName }
			});

	public static Exception PersistentSubscriptionDropped(string streamName, string groupName)
		=> new RpcException(
			new Status(
				StatusCode.Cancelled,
				$"Subscription group {groupName} on stream {streamName} was dropped."), new Metadata {
				{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.PersistentSubscriptionDropped },
				{ Constants.Exceptions.StreamName, streamName },
				{ Constants.Exceptions.GroupName, groupName }
			});

	public static Exception LoginNotFound(string loginName) =>
		new RpcException(new Status(StatusCode.NotFound, $"User '{loginName}' is not found."), new Metadata {
			{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.UserNotFound },
			{ Constants.Exceptions.LoginName, loginName }
		});

	public static Exception LoginConflict(string loginName) =>
		new RpcException(new Status(StatusCode.FailedPrecondition, "Conflict."), new Metadata {
			{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.UserConflict },
			{ Constants.Exceptions.LoginName, loginName }
		});

	public static Exception LoginTryAgain(string loginName) =>
		new RpcException(new Status(StatusCode.DeadlineExceeded, "Try again."), new Metadata {
			{ Constants.Exceptions.ExceptionKey, Constants.Exceptions.UserConflict },
			{ Constants.Exceptions.LoginName, loginName }
		});

	public static RpcException InvalidArgument<T>(T argument) =>
		new(new Status(StatusCode.InvalidArgument, $"'{argument}' is not a valid {typeof(T)}"));

	public static RpcException RequiredArgument<T>(string name, T argument) =>
		new(new Status(StatusCode.InvalidArgument, $"'{name}' is a required argument of type {typeof(T)}"));

	public static RpcException InvalidCombination<T>(T combination) where T : ITuple
		=> new(new Status(StatusCode.InvalidArgument, $"The combination of {combination} is invalid."));

	public static RpcException InvalidPositionException() =>
		new(new Status(StatusCode.InvalidArgument, "Trying to read from an invalid position."));
}
