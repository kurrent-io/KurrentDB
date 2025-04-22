// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using static KurrentDB.Core.Services.Transport.Grpc.RpcExceptions;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions {
	private static readonly Operation ReplayParkedOperation = new(Plugins.Authorization.Operations.Subscriptions.ReplayParked);

	public override async Task<ReplayParkedResp> ReplayParked(ReplayParkedReq request, ServerCallContext context) {
		var replayParkedMessagesSource = new TaskCompletionSource<ReplayParkedResp>();
		var correlationId = Guid.NewGuid();

		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user, ReplayParkedOperation, context.CancellationToken)) {
			throw AccessDenied();
		}

		string streamId = request.Options.StreamOptionCase switch {
			ReplayParkedReq.Types.Options.StreamOptionOneofCase.All => "$all",
			ReplayParkedReq.Types.Options.StreamOptionOneofCase.StreamIdentifier => request.Options.StreamIdentifier,
			_ => throw new InvalidOperationException()
		};

		long? stopAt = request.Options.StopAtOptionCase switch {
			ReplayParkedReq.Types.Options.StopAtOptionOneofCase.StopAt => request.Options.StopAt,
			ReplayParkedReq.Types.Options.StopAtOptionOneofCase.NoLimit => null,
			_ => throw new InvalidOperationException()
		};

		_publisher.Publish(new ClientMessage.ReplayParkedMessages(
			correlationId,
			correlationId,
			new CallbackEnvelope(HandleReplayParkedMessagesCompleted),
			streamId,
			request.Options.GroupName,
			stopAt,
			user));
		return await replayParkedMessagesSource.Task;

		void HandleReplayParkedMessagesCompleted(Message message) {
			switch (message) {
				case ClientMessage.NotHandled notHandled when TryHandleNotHandled(notHandled, out var ex):
					replayParkedMessagesSource.TrySetException(ex);
					return;
				case ClientMessage.ReplayMessagesReceived completed:
					switch (completed.Result) {
						case ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Success:
							replayParkedMessagesSource.TrySetResult(new ReplayParkedResp());
							return;
						case ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.DoesNotExist:
							replayParkedMessagesSource.TrySetException(
								PersistentSubscriptionDoesNotExist(streamId, request.Options.GroupName));
							return;
						case ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.AccessDenied:
							replayParkedMessagesSource.TrySetException(AccessDenied());
							return;
						case ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Fail:
							replayParkedMessagesSource.TrySetException(
								PersistentSubscriptionFailed(streamId, request.Options.GroupName, completed.Reason));
							return;

						default:
							replayParkedMessagesSource.TrySetException(UnknownError(completed.Result));
							return;
					}
				default:
					replayParkedMessagesSource.TrySetException(UnknownMessage<ClientMessage.ReplayMessagesReceived>(message));
					break;
			}
		}
	}
}
