// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using static EventStore.Core.Messages.ClientMessage.DeletePersistentSubscriptionToAllCompleted;
using static EventStore.Core.Messages.ClientMessage.DeletePersistentSubscriptionToStreamCompleted;
using StreamOptionOneofCase = EventStore.Client.PersistentSubscriptions.DeleteReq.Types.Options.StreamOptionOneofCase;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions {
	private static readonly Operation DeleteOperation = new Operation(Plugins.Authorization.Operations.Subscriptions.Delete);
	public override async Task<DeleteResp> Delete(DeleteReq request, ServerCallContext context) {

		var createPersistentSubscriptionSource = new TaskCompletionSource<DeleteResp>();
		var correlationId = Guid.NewGuid();

		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user,
			DeleteOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		string streamId = null;

		switch (request.Options.StreamOptionCase) {
			case StreamOptionOneofCase.StreamIdentifier: {
				streamId = request.Options.StreamIdentifier;
				_publisher.Publish(new ClientMessage.DeletePersistentSubscriptionToStream(
					correlationId,
					correlationId,
					new CallbackEnvelope(HandleDeletePersistentSubscriptionCompleted),
					streamId,
					request.Options.GroupName,
					user));
				break;
			}
			case StreamOptionOneofCase.All:
				streamId = SystemStreams.AllStream;
				_publisher.Publish(new ClientMessage.DeletePersistentSubscriptionToAll(
					correlationId,
					correlationId,
					new CallbackEnvelope(HandleDeletePersistentSubscriptionCompleted),
					request.Options.GroupName,
					user));
				break;
			default:
				throw new InvalidOperationException();
		}

		return await createPersistentSubscriptionSource.Task;

		void HandleDeletePersistentSubscriptionCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled && RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
				createPersistentSubscriptionSource.TrySetException(ex);
				return;
			}

			if (streamId != SystemStreams.AllStream) {
				if (message is ClientMessage.DeletePersistentSubscriptionToStreamCompleted completed) {
					switch (completed.Result) {
						case DeletePersistentSubscriptionToStreamResult.Success:
							createPersistentSubscriptionSource.TrySetResult(new DeleteResp());
							return;
						case DeletePersistentSubscriptionToStreamResult.Fail:
							createPersistentSubscriptionSource.TrySetException(
								RpcExceptions.PersistentSubscriptionFailed(streamId, request.Options.GroupName,
									completed.Reason));
							return;
						case DeletePersistentSubscriptionToStreamResult.DoesNotExist:
							createPersistentSubscriptionSource.TrySetException(
								RpcExceptions.PersistentSubscriptionDoesNotExist(streamId,
									request.Options.GroupName));
							return;
						case DeletePersistentSubscriptionToStreamResult.AccessDenied:
							createPersistentSubscriptionSource.TrySetException(RpcExceptions.AccessDenied());
							return;
						default:
							createPersistentSubscriptionSource.TrySetException(
								RpcExceptions.UnknownError(completed.Result));
							return;
					}
				}
				createPersistentSubscriptionSource.TrySetException(
					RpcExceptions.UnknownMessage<ClientMessage.DeletePersistentSubscriptionToStreamCompleted>(message));
			} else {
				if (message is ClientMessage.DeletePersistentSubscriptionToAllCompleted completedAll) {
					switch (completedAll.Result) {
						case DeletePersistentSubscriptionToAllResult.Success:
							createPersistentSubscriptionSource.TrySetResult(new DeleteResp());
							return;
						case DeletePersistentSubscriptionToAllResult.Fail:
							createPersistentSubscriptionSource.TrySetException(
								RpcExceptions.PersistentSubscriptionFailed(streamId, request.Options.GroupName,
									completedAll.Reason));
							return;
						case DeletePersistentSubscriptionToAllResult.DoesNotExist:
							createPersistentSubscriptionSource.TrySetException(
								RpcExceptions.PersistentSubscriptionDoesNotExist(streamId,
									request.Options.GroupName));
							return;
						case DeletePersistentSubscriptionToAllResult.AccessDenied:
							createPersistentSubscriptionSource.TrySetException(RpcExceptions.AccessDenied());
							return;
						default:
							createPersistentSubscriptionSource.TrySetException(
								RpcExceptions.UnknownError(completedAll.Result));
							return;
					}
				}
				createPersistentSubscriptionSource.TrySetException(
					RpcExceptions.UnknownMessage<ClientMessage.DeletePersistentSubscriptionToAllCompleted>(message));
			}

		}
	}
}
