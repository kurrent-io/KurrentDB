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
	private static readonly Operation TruncateParkedOperation = new(Plugins.Authorization.Operations.Subscriptions.ReplayParked);

	public override async Task<TruncateParkedResp> TruncateParked(TruncateParkedReq request, ServerCallContext context) {
		var truncateParkedSource = new TaskCompletionSource<TruncateParkedResp>(TaskCreationOptions.RunContinuationsAsynchronously);
		var correlationId = Guid.NewGuid();

		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user, TruncateParkedOperation, context.CancellationToken)) {
			throw AccessDenied();
		}

		string streamId = request.Options.StreamOptionCase switch {
			TruncateParkedReq.Types.Options.StreamOptionOneofCase.All => "$all",
			TruncateParkedReq.Types.Options.StreamOptionOneofCase.StreamIdentifier => request.Options.StreamIdentifier,
			_ => throw new InvalidOperationException()
		};

		long? stopAt = request.Options.StopAtOptionCase switch {
			TruncateParkedReq.Types.Options.StopAtOptionOneofCase.StopAt => request.Options.StopAt,
			TruncateParkedReq.Types.Options.StopAtOptionOneofCase.NoLimit => null,
			_ => throw new InvalidOperationException()
		};

		_publisher.Publish(new ClientMessage.TruncateParkedMessages(
			correlationId,
			correlationId,
			new CallbackEnvelope(HandleTruncateParkedCompleted),
			streamId,
			request.Options.GroupName,
			stopAt,
			user));
		return await truncateParkedSource.Task;

		void HandleTruncateParkedCompleted(Message message) {
			switch (message) {
				case ClientMessage.NotHandled notHandled when TryHandleNotHandled(notHandled, out var ex):
					truncateParkedSource.TrySetException(ex);
					return;
				case ClientMessage.TruncateParkedMessagesCompleted completed:
					switch (completed.Result) {
						case ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.Success:
							truncateParkedSource.TrySetResult(new TruncateParkedResp());
							return;
						case ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.DoesNotExist:
							truncateParkedSource.TrySetException(
								PersistentSubscriptionDoesNotExist(streamId, request.Options.GroupName));
							return;
						case ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.AccessDenied:
							truncateParkedSource.TrySetException(AccessDenied());
							return;
						case ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.Fail:
							truncateParkedSource.TrySetException(
								PersistentSubscriptionFailed(streamId, request.Options.GroupName, completed.Reason));
							return;

						default:
							truncateParkedSource.TrySetException(UnknownError(completed.Result));
							return;
					}
				default:
					truncateParkedSource.TrySetException(UnknownMessage<ClientMessage.TruncateParkedMessagesCompleted>(message));
					break;
			}
		}
	}
}
